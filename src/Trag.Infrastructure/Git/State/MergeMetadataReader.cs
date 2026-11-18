using System.Security.Cryptography;
using System.Text;
using Trag.Domain;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Infrastructure.Git.State;

// The state of a merge in progress: its heads, its message and a fingerprint over every file Git wrote for it, so a
// change made outside Trag while a write is in flight is visible. A message in another encoding is read as it decodes.
// Git writes a large MERGE_MSG on its own (a line per conflicted file, a line per merged commit with merge.log), so no
// file's size makes the repository unreadable: the fingerprint covers every byte, and the text is kept up to the largest
// message a commit takes, cut at its last whole line.
internal static class MergeMetadataReader
{
    public static async Task<RepositoryMerge?> ReadAsync(string gitDirectory, RootRefs roots, CancellationToken ct)
    {
        if (!File.Exists(Path.Combine(gitDirectory, "MERGE_HEAD"))) return null;
        var values = new List<string>();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var name in new[] { "MERGE_HEAD", "ORIG_HEAD", "MERGE_MSG", "MERGE_MODE", "MERGE_AUTOSTASH" })
        {
            var path = Path.Combine(gitDirectory, name);
            // ORIG_HEAD and MERGE_AUTOSTASH are refs, which a reftable repository keeps in its tables rather than as files.
            var root = name is "ORIG_HEAD" or "MERGE_AUTOSTASH" ? roots[name] : null;
            var exists = root is not null || File.Exists(path);
            hash.AppendData([exists ? (byte)1 : (byte)0]);
            if (root is not null) hash.AppendData(Encoding.UTF8.GetBytes(root));
            values.Add(root ?? (exists ? (await StateFile.ReadAsync(path, hash, GitLimits.CommitMessageBytes, ct).ConfigureAwait(false)).TrimEnd('\r', '\n') : ""));
            hash.AppendData([0]);
        }
        var supported = GitObjectId.IsValid(values[0]) && GitObjectId.IsValid(values[1]);
        var limitation = !supported ? MergeText.UnsupportedMerge
            : roots["MERGE_AUTOSTASH"] is not null ? MergeText.MergeAutostash : "";
        return new(GitObjectId.IsValid(values[0]) ? values[0] : null, GitObjectId.IsValid(values[1]) ? values[1] : null,
            values[2], Convert.ToHexString(hash.GetHashAndReset()), limitation);
    }
}
