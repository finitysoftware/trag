using Trag.Application.Ports.Git;
using Trag.Domain.Diff;
using Trag.Domain.Merge;
using Trag.Domain.Status;

namespace Trag.TestSupport;

public sealed class FakeFileDiffReader : IFileDiffReader
{
    public Func<FileStatus, bool, bool, FileDiff>? Diff { get; set; }
    public List<(FileStatus File, bool FromIndex, bool Preview)> Requests { get; } = [];
    public AsyncGate Gate { get; } = new();

    public async Task<FileDiff> ReadAsync(string repository, FileStatus file, bool fromIndex, bool preview, CancellationToken cancellationToken)
    {
        Requests.Add((file, fromIndex, preview));
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return Diff?.Invoke(file, fromIndex, preview) ?? new FileDiff([], [new('+', "+" + (fromIndex ? "staged" : "unstaged"), null, 1)], DiffContentKind.Text, "");
    }

    // The working text of a conflicted file, by path; by default one conflict between two one-line sides.
    public Func<FileStatus, ConflictText> Conflict { get; set; } = _ => new(string.Join('\n', "a", "<<<<<<< HEAD", "mine", "=======", "theirs", ">>>>>>> incoming", "z", ""), "hash");
    public List<FileStatus> ConflictReads { get; } = [];

    public Task<ConflictText> ReadConflictAsync(string repository, FileStatus file, CancellationToken ct)
    {
        ConflictReads.Add(file);
        return Task.FromResult(Conflict(file));
    }
}
