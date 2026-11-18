using Trag.Infrastructure.Accounts.Credentials;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Platform.Storage;

namespace Trag.Infrastructure.Git.Sync;

// Submodules checked out at the commits the index records, the missing ones cloned first, one top-level submodule per Git
// run with those inside it. Each clone asks for credentials when it starts, so each run gets a credential broker of its
// own, whose time and answers cover that one submodule. A submodule's address comes from the repository, so a local one
// is cloned only as Git allows or the user's own configuration says; Git's progress keeps a long clone from being taken
// for a silent one. What comes back is Git's words for the submodules that failed, or nothing. A clone that Cancel or the
// quiet limit stops leaves a module folder without commits, which Git would take for the submodule from then on, so the
// run takes back a module folder it created and empties the submodule's folder; a module folder that was there before
// holds the submodule's own history and is never touched.
internal sealed class SubmoduleTransfer(GitProcessRunner runner, string? askPassExecutable)
{
    private readonly PresetGitRunner reading = new(runner, LocalGitPreset.Reading);

    public async Task<string> UpdateAsync(string root, IReadOnlyList<string> paths, Func<CancellationToken, Task<CredentialBroker?>> brokers,
        IProgress<string>? progress, CancellationToken ct)
    {
        var failures = new List<string>();
        var configured = await runner.RunAsync(root, ["config", "--get", "protocol.file.allow"], ct).ConfigureAwait(false);
        var fileProtocol = configured.ExitCode == 0 ? GitBytes.Text(configured.StandardOutput) : null;
        var modules = GitBytes.Text((await reading.RunAsync(root, ["rev-parse", "--path-format=absolute", "--git-path", "modules"], ct).ConfigureAwait(false)).StandardOutput);
        var listed = await SubmodulePaths.ListedAsync(reading, root, ct).ConfigureAwait(false);
        foreach (var path in paths)
        {
            var env = TransportEnvironment.Create(null);
            var setup = TransportEnvironment.Setup(null);
            await using var broker = await brokers(ct).ConfigureAwait(false);
            if (broker is not null) setup = TransportEnvironment.Authenticated(env, setup, broker, askPassExecutable!);
            var clone = listed.FirstOrDefault(submodule => submodule.Path == path) is { } named
                ? NewClone.Of(Path.Combine(modules, named.Name), Path.Combine(root, path)) : null;
            GitResult result;
            try
            {
                result = await runner.RunAsync(root, [.. TransportEnvironment.ForSubmodules(setup, fileProtocol), "submodule", "update", "--init", "--recursive", "--progress", "--", path],
                    ct, env, standardErrorProgress: broker is null ? progress : null, quietLimit: SyncTimeouts.Quiet).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                clone?.TakeBack();
                throw;
            }
            if (result.ExitCode == 0) continue;
            var text = GitOutputText.Raw(result);
            failures.Add(GitOutputRedaction.Redact(await AddressPasswordsHiddenAsync(root, broker is null ? text : broker.Redact(text), ct).ConfigureAwait(false)).Trim());
        }
        return string.Join("\n", failures);
    }

    // A password the user wrote into a submodule's address, which Git copied into the configuration when it initialized
    // the submodule, is hidden from the details, the only place this text goes.
    private async Task<string> AddressPasswordsHiddenAsync(string root, string text, CancellationToken ct)
    {
        var addresses = await reading.RunAsync(root, ["config", "--null", "--get-regexp", "^submodule\\..*\\.url$"], ct, allowMissing: true).ConfigureAwait(false);
        foreach (var (_, address) in GitConfigParser.Parse(addresses.StandardOutput))
            if (address is not null && CredentialRedactor.ForAddress(address) is { } hidden) text = hidden.Redact(text);
        return text;
    }

    // A submodule the run is about to clone: Git clones only where no module folder exists yet and the submodule's
    // folder is missing or empty.
    private sealed record NewClone(string Module, string Folder)
    {
        public static NewClone? Of(string module, string folder) =>
            Directory.Exists(module) || Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any() ? null : new(module, folder);

        public void TakeBack()
        {
            ClonedFolder.TakeBack(Module, existed: false);
            ClonedFolder.TakeBack(Folder, existed: true);
        }
    }
}
