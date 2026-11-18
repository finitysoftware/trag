using Trag.Application.Ports.Git;
using Trag.Domain.Diff;
using Trag.Domain.Merge;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Read;

// The diff of one file, as the shell asks for it. This is where a path is refused before any content is read, and where
// the one reader that can answer for this file is chosen. The guards run in this order on purpose: the path's own
// bytes, what the file is, where it leads, and only then what it contains.
public sealed class CliFileDiffReader : IFileDiffReader
{
    private readonly ConflictPreviewReader conflicts;
    private readonly FileDiffMetadataReader records;
    private readonly DiffSizeInspector sizes;
    private readonly LargeFileDiffReader large;
    private readonly FilePatchReader patches;
    private readonly LfsFileDiff lfs;
    private readonly PresetGitRunner submodules;
    private readonly ConflictMarkerSizes markers;

    public CliFileDiffReader(GitProcessRunner runner)
    {
        var git = new PresetGitRunner(runner, LocalGitPreset.Reading);
        var blobs = new BlobPreviewReader(git);
        conflicts = new(blobs);
        records = new(git);
        sizes = new(git);
        large = new(blobs);
        patches = new(git, blobs);
        submodules = git;
        lfs = new(git);
        markers = new(git);
    }

    public Task<FileDiff> ReadAsync(string repository, FileStatus file, bool fromIndex, bool preview, CancellationToken ct) =>
        ReadTimeout.RunAsync(TimeSpan.FromSeconds(15), ct => ReadBackgroundAsync(repository, file, fromIndex, preview, ct), ct);

    // The whole conflicted working file for the merge view's blocks, behind the same path checks as a diff. A submodule's
    // sides are commits, with no text to show.
    public Task<ConflictText> ReadConflictAsync(string repository, FileStatus file, CancellationToken ct) => Task.Run(() =>
        file.IsSubmodule ? Task.FromResult(ConflictText.Unavailable(MergeText.SubmoduleConflict))
        : file.Path.IsDisplayable ? ConflictTextAsync(repository, file.Path, ct)
            : Task.FromResult(ConflictText.Unavailable(MergeText.NotTextPath)), ct);

    private async Task<ConflictText> ConflictTextAsync(string repository, GitPath path, CancellationToken ct) =>
        await ConflictFileText.ReadAsync(repository, path.Display,
            ConflictMarkerSizes.Of(await markers.ReadAsync(repository, [path], ct).ConfigureAwait(false), path), ct).ConfigureAwait(false);

    private async Task<FileDiff> ReadBackgroundAsync(string root, FileStatus file, bool fromIndex, bool preview, CancellationToken ct)
    {
        if (!file.Path.IsDisplayable || file.OriginalPath is { IsDisplayable: false })
            return new([], [], DiffContentKind.Unavailable, FileDiffText.NonUtf8Path);
        var path = file.Path.Display;
        var original = file.OriginalPath?.Display;
        if (file.IsSubmodule)
            return SubmoduleLog.Of([], (await submodules.RunAsync(root, ["diff", "--no-color", "--no-ext-diff", "--submodule=log", "--ignore-submodules=untracked", .. fromIndex ? new[] { "--cached" } : [], "--", path], ct)).StandardOutput);
        // Git lists a folder whole only when it is another repository, which has no diff here.
        if (path.EndsWith('/'))
            return new([], [], DiffContentKind.Unavailable, FileDiffText.InnerRepository);
        var paths = fromIndex && original is not null ? new[] { original, path } : [path];
        var fullPath = RepositoryPathGuard.SafePath(root, path);
        // Whatever reads the working file must not follow a symlink or junction outside the repository, even for a
        // preview: a working-tree diff does, and so does a conflict, whose markers exist only in the working file.
        if ((!fromIndex || file.IsConflict) && RepositoryPathGuard.HasLink(root, fullPath))
            return new([], [], DiffContentKind.Unavailable, FileDiffText.Link);
        if (file.IsConflict)
            return await conflicts.ReadAsync(file, fullPath, ct);
        var metadata = await records.ReadAsync(root, file, fromIndex, paths, ct);
        var size = await sizes.MeasureAsync(root, fullPath, fromIndex, metadata, ct);
        if (size == DiffSize.NotDownloaded)
            return new(metadata, [], DiffContentKind.Unavailable, FileDiffText.NotDownloaded);
        if (size == DiffSize.Large)
            return !fromIndex && await lfs.OfLargeWorkingFileAsync(root, fullPath, metadata, ct) is { } lfsFile
                ? lfsFile : await large.ReadAsync(root, fullPath, fromIndex, preview, metadata, ct);
        return await lfs.OfAsync(root, await patches.ReadAsync(root, file, fullPath, fromIndex, preview, metadata, paths, ct), preview, ct);
    }
}
