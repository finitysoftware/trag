using System.Security.Cryptography;
using System.Text;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;
using Trag.Domain.Sync;
using Trag.Domain.Naming;

namespace Trag.Infrastructure.Git.Sync;

// What Git is configured to do with one remote, and what about that configuration Trag refuses to work with. The
// limitation is assigned in order and the later assignment wins: that order is behaviour, pinned by
// RemoteConfigurationLimitationTests. The identity hashes everything a selected account is bound to.
internal sealed class RemoteConfigurationReader(PresetGitRunner git, GitRefNameValidator refNames)
{
    public async Task<RemoteConfiguration> ReadConfigurationAsync(RepositorySnapshot snapshot, string remote, CancellationToken ct)
    {
        if (snapshot.Remote(remote) is null || !await refNames.IsValidRemoteNameAsync(snapshot.Root, remote, ct).ConfigureAwait(false))
            throw new ArgumentException("Choose an existing remote.");
        var config = await git.RunAsync(snapshot.Root, ["config", "--null", "--get-regexp",
            "^(remote\\..*\\.(url|pushurl|fetch|mirror|uploadpack|receivepack|tagopt|prune|proxy|proxyauthmethod|lfsurl|lfspushurl)|core\\.sshcommand|ssh\\.variant|url\\..*\\.(insteadof|pushinsteadof))$"], ct, allowMissing: true).ConfigureAwait(false);
        var entries = new GitConfiguration(GitConfigParser.Parse(config.StandardOutput));
        var fetch = entries.Values("remote." + remote + ".url");
        var push = entries.Values("remote." + remote + ".pushurl");
        var fetchAddresses = await ReadAddressesAsync(snapshot.Root, remote, false, ct).ConfigureAwait(false);
        var pushAddresses = await ReadAddressesAsync(snapshot.Root, remote, true, ct).ConfigureAwait(false);
        var limitation = !HasSingleDestination(fetch, push, fetchAddresses, pushAddresses)
            ? SyncOutcomeText.MultipleUrls : "";
        var mirror = await git.RunAsync(snapshot.Root, ["config", "--type=bool", "--get", "remote." + remote + ".mirror"], ct, allowMissing: true).ConfigureAwait(false);
        if (GitBytes.StrictText(mirror.StandardOutput) == "true") limitation = SyncOutcomeText.Mirror;
        var (mappings, hasUnsupported) = await ReadMappingsAsync(snapshot.Root, remote, entries, ct).ConfigureAwait(false);
        if (hasUnsupported) limitation = SyncOutcomeText.UnsupportedMapping;
        if (mappings.Count == 0 || mappings.Count > GitLimits.FetchMappings) limitation = SyncOutcomeText.MappingCount;
        if (snapshot.Remotes.FirstOrDefault(other => other.Name != remote && RemoteNames.Overlap(other.Name, remote)) is { } overlapping)
            limitation = SyncOutcomeText.OverlappingNames(remote, overlapping.Name);
        if (snapshot.GitDirectory is { } gitDirectory && File.Exists(Path.Combine(gitDirectory, "shallow"))) limitation = SyncOutcomeText.Shallow;
        var (ssh, variant) = ReadSsh(entries);
        if (variant is not ("ssh" or "plink" or "tortoiseplink")) limitation = SyncOutcomeText.SshVariant;
        var hash = ConfigurationId(entries, fetchAddresses, pushAddresses, ssh, variant);
        return new(new(remote, AddressRedactor.Redact(fetchAddresses[0]), AddressRedactor.Redact(pushAddresses[0]), hash, limitation),
            mappings.ToArray(), ssh, variant, fetchAddresses[0], pushAddresses[0],
            entries.TransportOptions(remote));
    }

    private static bool HasSingleDestination(string[] fetch, string[] push, string[] effectiveFetch, string[] effectivePush) =>
        fetch.Length == 1 && push.Length <= 1 && effectiveFetch.Length == 1 && effectivePush.Length == 1;

    private async Task<string[]> ReadAddressesAsync(string root, string remote, bool push, CancellationToken ct)
    {
        string[] arguments = push ? ["remote", "get-url", "--push", "--all", "--", remote] : ["remote", "get-url", "--all", "--", remote];
        var result = await git.RunAsync(root, arguments, ct).ConfigureAwait(false);
        return GitBytes.StrictText(result.StandardOutput).Split('\n');
    }

    private async Task<(List<RemoteRefspecMapping> Mappings, bool HasUnsupported)> ReadMappingsAsync(
        string root, string remote, GitConfiguration entries, CancellationToken ct)
    {
        var mappings = new List<RemoteRefspecMapping>();
        var hasUnsupported = false;
        foreach (var refspec in entries.Values("remote." + remote + ".fetch"))
        {
            var mapping = RemoteRefspecMapping.Parse(refspec, remote);
            if (mapping is null || !await IsValidMappingAsync(root, mapping, ct).ConfigureAwait(false))
                hasUnsupported = true;
            else
                mappings.Add(mapping);
        }
        return (mappings, hasUnsupported);
    }

    private async Task<bool> IsValidMappingAsync(string root, RemoteRefspecMapping mapping, CancellationToken ct) =>
        await refNames.IsValidRefAsync(root, mapping.Source, true, ct).ConfigureAwait(false)
        && await refNames.IsValidRefAsync(root, mapping.Destination, true, ct).ConfigureAwait(false);

    private static (string Command, string Variant) ReadSsh(GitConfiguration entries)
    {
        var ssh = Environment.GetEnvironmentVariable("GIT_SSH_COMMAND") ?? entries.Value("core.sshcommand");
        ssh ??= Environment.GetEnvironmentVariable("GIT_SSH") is { } program ? "'" + program.Replace("'", "'\\''", StringComparison.Ordinal) + "'" : "ssh";
        var variant = Environment.GetEnvironmentVariable("GIT_SSH_VARIANT") ?? entries.Value("ssh.variant") ?? "auto";
        if (variant is "auto") variant = VariantOf(ssh);
        return (ssh, variant);
    }

    // As Git tells it without ssh.variant: by the name of the program the command's first word runs, quoted or not.
    private static string VariantOf(string command)
    {
        var text = command.TrimStart();
        var quoted = text.Length != 0 && text[0] is '\'' or '"';
        var end = quoted ? text.IndexOf(text[0], 1) : text.IndexOfAny([' ', '\t']);
        var program = quoted ? text[1..end] : end < 0 ? text : text[..end];
        var name = program[(program.LastIndexOfAny(['/', '\\']) + 1)..].ToLowerInvariant();
        return name is "plink" or "plink.exe" ? "plink" : name is "tortoiseplink" or "tortoiseplink.exe" ? "tortoiseplink" : "ssh";
    }

    private static string ConfigurationId(GitConfiguration entries, string[] fetchAddresses, string[] pushAddresses, string ssh, string variant)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', entries.Entries.Select(item => item.Key + "\n" + item.Value))
            + "\0" + string.Join('\0', fetchAddresses) + "\0" + string.Join('\0', pushAddresses) + "\0" + ssh + "\0" + variant)));
    }
}
