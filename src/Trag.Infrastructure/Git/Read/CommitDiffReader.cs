using Trag.Domain.Diff;
using Trag.Domain.History;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using Trag.Domain;

namespace Trag.Infrastructure.Git.Read;

// The diff of one file inside one commit, against the same parent the file list used. A file too large for a whole
// diff is offered as a bounded preview of the side that has content.
internal sealed class CommitDiffReader(PresetGitRunner git)
{
    private readonly DiffSizeInspector sizes = new(git);
    private readonly LfsFileDiff lfs = new(git);

    public Task<FileDiff> ReadDiffAsync(string root, CommitDetails commit, FileDiffMetadata file, bool preview, CancellationToken ct) => ReadTimeout.RunAsync(TimeSpan.FromSeconds(15), async ct =>
    {
        GitObjectId.Validate(commit.Commit.Id);
        foreach (var parent in commit.Parents) GitObjectId.Validate(parent);
        GitObjectId.Validate(file.OldObject);
        GitObjectId.Validate(file.NewObject);
        if (file.IsSubmodule)
            return SubmoduleLog.Of([file], (await git.RunAsync(root, CommitDiffArguments.For(commit, ["--patch", "--submodule=log"], [file.Path.Display]), ct).ConfigureAwait(false)).StandardOutput);
        if (!file.Path.IsDisplayable || file.OriginalPath is { IsDisplayable: false }) return new([file], [], DiffContentKind.Unavailable, FileDiffText.NonUtf8Path);
        string[] paths = file.OriginalPath is null ? [file.Path.Display] : [file.OriginalPath.Display, file.Path.Display];
        var size = await sizes.MeasureObjectsAsync(root, file.Objects.Distinct().ToArray(), ct).ConfigureAwait(false);
        if (size == DiffSize.NotDownloaded) return new([file], [], DiffContentKind.Unavailable, FileDiffText.NotDownloaded);
        if (size == DiffSize.Large) return await PreviewAsync(root, file, preview, ct).ConfigureAwait(false);
        var patch = await git.RunAsync(root, ["-c", "diff.algorithm=myers", .. CommitDiffArguments.For(commit, ["--patch", "--find-renames=50%", DiffContext.WholeFile], paths)], ct, prefix: DiffLimits.ByteLimit).ConfigureAwait(false);
        return await lfs.OfAsync(root, FileDiffParser.Patch([file], patch.StandardOutput, preview, patch.OutputTruncated), preview, ct).ConfigureAwait(false);
    }, ct);

    private async Task<FileDiff> PreviewAsync(string root, FileDiffMetadata file, bool preview, CancellationToken ct)
    {
        if (!preview) return ContentPreview.Offer([file]);
        var content = await git.RunAsync(root, ["cat-file", "blob", file.PreviewObject!], ct, prefix: DiffLimits.PreviewBytes).ConfigureAwait(false);
        return ContentPreview.Of([file], content.StandardOutput);
    }
}
