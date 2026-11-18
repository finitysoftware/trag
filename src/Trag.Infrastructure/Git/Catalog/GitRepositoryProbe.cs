using Trag.Application.Ports.Catalog;
using Trag.Domain.Catalog;
using Trag.Domain.Naming;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Catalog;

// Whether a stored link still points at a working copy, and what the picker shows next to it. A bare repository or a
// folder that is no longer a repository is an answer, not a failure.
internal sealed class GitRepositoryProbe(GitProcessRunner runner) : IRepositoryProber
{
    public Task<RepositoryProbe> ProbeAsync(string path, CancellationToken ct) => Task.Run(async () =>
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return new RepositoryProbe(RepositoryLinkStatus.Missing);
        // rev-parse answers in argument order and stops at the first question it cannot answer: a bare repository is in its
        // Git data and fails on the working tree, a working copy's .git folder and the folders in it fail on it too after
        // saying they are no bare repository, and a plain folder fails at once.
        var location = await runner.RunAsync(path, ["rev-parse", "--is-inside-git-dir", "--is-bare-repository", "--show-toplevel"], ct).ConfigureAwait(false);
        // The working tree's path is the last answer and may itself hold a line break, as a Linux folder name can.
        var lines = GitBytes.Text(location.StandardOutput).Split('\n', 3).Select(line => line.TrimEnd('\r')).ToArray();
        if (GitOwnership.Refused(location)) return new RepositoryProbe(RepositoryLinkStatus.Untrusted);
        if (GitRepositoryFormat.Unknown(location)) return new RepositoryProbe(RepositoryLinkStatus.NeedsNewerGit);
        if (lines is [_, "true", ..]) return new RepositoryProbe(RepositoryLinkStatus.Bare);
        // Git cannot answer in a folder of Git data whose working tree is gone, as a removed submodule's, nor, under
        // safe.bareRepository=explicit, in a bare repository it is not pointed at; neither is a folder to initialize.
        if (location.ExitCode != 0 || lines is not [_, "false", { Length: > 0 } top])
            return new RepositoryProbe(lines is ["true", ..] || InGitData(path) ? RepositoryLinkStatus.GitDirectory
                : GitBareRepository.Refused(location) ? RepositoryLinkStatus.Bare
                : RepositoryLinkStatus.NotRepository);
        var branch = await runner.RunAsync(path, ["symbolic-ref", "--quiet", "HEAD"], ct).ConfigureAwait(false);
        var remote = await runner.RunAsync(path, ["remote", "get-url", "origin"], ct).ConfigureAwait(false);
        return new RepositoryProbe(RepositoryLinkStatus.Valid, branch.ExitCode == 0 ? RefNames.LocalBranch(GitBytes.FirstLine(branch.StandardOutput)) : null,
            remote.ExitCode == 0 ? CatalogAddressRedactor.Redact(GitBytes.FirstLine(remote.StandardOutput)) : null, Path.GetFullPath(top));
    }, ct);

    private static bool InGitData(string path) =>
        Path.GetFullPath(path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".git", StringComparer.OrdinalIgnoreCase);
}
