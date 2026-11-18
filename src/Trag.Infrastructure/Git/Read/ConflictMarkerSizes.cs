using System.Globalization;
using System.Text;
using Trag.Domain.Merge;
using Trag.Domain.Paths;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Read;

// How long the conflict markers Git writes in each file are: the file's conflict-marker-size attribute where it is a
// positive number, as Git's merge reads it, seven otherwise. Every path is asked in one check-attr, so a merge with a
// thousand conflicts costs one Git process, not a thousand.
internal sealed class ConflictMarkerSizes(PresetGitRunner git)
{
    public async Task<IReadOnlyDictionary<string, int>> ReadAsync(string root, IEnumerable<GitPath> paths, CancellationToken ct)
    {
        var asked = paths.Where(path => path.IsDisplayable).DistinctBy(path => path.Key).ToArray();
        var sizes = new Dictionary<string, int>(StringComparer.Ordinal);
        if (asked.Length == 0) return sizes;
        var input = asked.SelectMany(path => path.Bytes.Append((byte)0)).ToArray();
        var output = (await git.RunAsync(root, ["check-attr", "-z", "--stdin", "conflict-marker-size"], ct, input).ConfigureAwait(false)).StandardOutput;
        var fields = NulRecordReader.Records(output).ToArray();
        // Path, attribute, value: the value is "unspecified", "unset", "set" or the number the attribute gives.
        for (var i = 0; i + 2 < fields.Length; i += 3)
            if (int.TryParse(Encoding.ASCII.GetString(fields[i + 2]), NumberStyles.None, CultureInfo.InvariantCulture, out var size) && size > 0)
                sizes[new GitPath(fields[i]).Key] = size;
        return sizes;
    }

    public static int Of(IReadOnlyDictionary<string, int> sizes, GitPath path) => sizes.GetValueOrDefault(path.Key, ConflictDocument.DefaultMarkerSize);
}
