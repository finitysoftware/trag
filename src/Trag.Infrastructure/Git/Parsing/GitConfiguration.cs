using Trag.Domain.Status;

namespace Trag.Infrastructure.Git.Parsing;

// The entries as most readers want them, a key with no value as an empty one; Flag tells the two apart, as Git does.
internal sealed class GitConfiguration(IReadOnlyList<(string Key, string? Value)> listed)
{
    public IReadOnlyList<(string Key, string Value)> Entries { get; } = [.. listed.Select(entry => (entry.Key, entry.Value ?? ""))];
    public string? Value(string key) => Entries.LastOrDefault(entry => entry.Key == key).Value;
    // Git LFS installed is the filter process git lfs install configures; without it Git would commit an LFS file itself.
    public bool LfsInstalled => Value("filter.lfs.process") is { Length: > 0 };
    // How Git signs a commit: commit.gpgsign and the format of the key.
    public CommitSigning Signing => CommitSignings.Of(Flag("commit.gpgsign"), Value("gpg.format"));

    // A boolean by Git's rule: unset is false, a key with no value true, an empty value false, and the words or any whole
    // number but zero true.
    public bool Flag(string key) => listed.Where(entry => entry.Key == key).Select(entry => entry.Value).ToArray() is [.., var value]
        && (value is null || value.ToLowerInvariant() is "true" or "yes" or "on" || long.TryParse(value, out var number) && number != 0);
    public string[] Values(string key) => Entries.Where(entry => entry.Key == key).Select(entry => entry.Value).ToArray();

    public IEnumerable<string> RemoteNames() => Entries
        .Where(entry => entry.Key.StartsWith("remote.", StringComparison.Ordinal) && entry.Key.EndsWith(".url", StringComparison.Ordinal))
        .Select(entry => entry.Key[7..^4]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

    public (string Key, string Value)[] TransportOptions(string remote)
    {
        var prefix = "remote." + remote + ".";
        return Entries.Where(entry => entry.Key == prefix + "receivepack" || entry.Key == prefix + "tagopt" || entry.Key == prefix + "prune" || entry.Key == prefix + "proxy" || entry.Key == prefix + "proxyauthmethod"
                || entry.Key == prefix + "lfsurl" || entry.Key == prefix + "lfspushurl")
            .Select(entry => (entry.Key[prefix.Length..], entry.Value)).ToArray();
    }
}
