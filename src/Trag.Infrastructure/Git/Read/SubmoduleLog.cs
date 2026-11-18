using Trag.Domain.Diff;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.Git.Read;

// A submodule's change as Git summarizes it (--submodule=log): the commits between the one recorded and the one it is
// at, and a line when its own working tree has changes. Git says nothing when only files it does not track changed.
internal static class SubmoduleLog
{
    public static FileDiff Of(IReadOnlyList<FileDiffMetadata> metadata, byte[] output) =>
        new(metadata, [], DiffContentKind.Unavailable, GitBytes.Text(output).Trim() is { Length: > 0 } log ? log : FileDiffText.Submodule);
}
