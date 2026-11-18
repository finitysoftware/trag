using Trag.Domain.Merge;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Writes;

// Each write WriteNeeds names, with staging and committing for every other write, against every state a repository can be in, as the writer and the Git writer ask before a write
// starts. An outcome waiting for review is the application's (RepositoryWriterTests); a recovery index is a limitation.
public sealed class WriteNeedsMatrixTests
{
    private static readonly (string Group, string Name, Func<RepositorySnapshot, GitWriteRequest> Build)[] Writes =
    [
        ("write", "stage", s => new StageRequest(s, [Snapshots.Path("a")])),
        ("write", "commit", s => new CommitRequest(s, "m")),
        ("merge", "finish-merge", s => new CommitRequest(s, "m", FinishesMerge: true)),
        ("merge", "abort-merge", s => new MergeAbortRequest(s)),
        ("rebase", "continue-rebase", s => new RebaseContinueRequest(s, "m")),
        ("rebase", "skip-rebase", s => new RebaseSkipRequest(s)),
        ("rebase", "abort-rebase", s => new RebaseAbortRequest(s)),
        ("pick", "finish-pick", s => new PickContinueRequest(s)),
        ("pick", "abort-pick", s => new PickAbortRequest(s)),
        ("resolve", "stage-resolution", s => new StageRequest(s, [Snapshots.Path("a")], Resolution: true)),
        ("resolve", "take-side", s => new ConflictSideRequest(s, Snapshots.Path("a"), ConflictChoice.Mine)),
        ("resolve", "take-block", s => new ConflictBlockRequest(s, Snapshots.Path("a"), 0, ConflictChoice.Both, "f00d")),
        ("resolve", "reopen", s => new ReopenConflictRequest(s, Snapshots.Path("a"))),
        ("resolve", "merge-tool", s => new MergeToolRequest(s, Snapshots.Path("a"), false))
    ];

    private static readonly RepositorySnapshot Merging = Snapshots.Merging();
    private static readonly RepositorySnapshot Picking = Snapshots.Create(operation: RepositoryOperation.CherryPick) with { PickHead = Snapshots.Id('b') };

    private static readonly Dictionary<string, RepositorySnapshot> States = new()
    {
        ["none"] = Snapshots.Create(),
        ["detached"] = Snapshots.Create(branch: null),
        ["unborn"] = Snapshots.Create(unborn: true),
        ["recovery-index"] = Snapshots.Create(limitations: [LimitationText.RecoveryIndex]),
        ["merge"] = Merging,
        ["merge-detached"] = Merging with { Branch = null },
        ["merge-limited"] = Merging with { Merge = Merging.Merge! with { Limitation = "autostash" } },
        ["merge-recovery-index"] = Merging with { Limitations = [LimitationText.RecoveryIndex] },
        ["rebase"] = Snapshots.Rebasing(),
        ["rebase-limited"] = Snapshots.Rebasing(Snapshots.Rebase(limitation: "exec")),
        ["rebase-recovery-index"] = Snapshots.Rebasing() with { Limitations = [LimitationText.RecoveryIndex] },
        ["cherry-pick"] = Picking,
        ["revert"] = Picking with { Operation = RepositoryOperation.Revert },
        ["cherry-pick-recovery-index"] = Picking with { Limitations = [LimitationText.RecoveryIndex] },
        ["revert-without-its-head"] = Snapshots.Create(operation: RepositoryOperation.Revert),
        ["conflict"] = Snapshots.Create(operation: RepositoryOperation.Conflict),
        ["conflict-recovery-index"] = Snapshots.Create(operation: RepositoryOperation.Conflict, limitations: [LimitationText.RecoveryIndex]),
        ["sequencer"] = Snapshots.Create(operation: RepositoryOperation.Sequencer),
        ["bisect"] = Snapshots.Create(operation: RepositoryOperation.Bisect),
        ["apply-mailbox"] = Snapshots.Create(operation: RepositoryOperation.ApplyMailbox)
    };

    [Theory]
    [InlineData("none", "write")]
    [InlineData("detached", "write")]
    [InlineData("unborn", "write")]
    [InlineData("recovery-index", "")]
    [InlineData("merge", "merge resolve")]
    [InlineData("merge-detached", "")]
    [InlineData("merge-limited", "")]
    [InlineData("merge-recovery-index", "")]
    [InlineData("rebase", "rebase resolve")]
    [InlineData("rebase-limited", "")]
    [InlineData("rebase-recovery-index", "")]
    [InlineData("cherry-pick", "pick resolve")]
    [InlineData("revert", "pick resolve")]
    [InlineData("cherry-pick-recovery-index", "")]
    [InlineData("revert-without-its-head", "")]
    [InlineData("conflict", "resolve")]
    [InlineData("conflict-recovery-index", "")]
    [InlineData("sequencer", "")]
    [InlineData("bisect", "")]
    [InlineData("apply-mailbox", "")]
    public void WhenTheRepositoryIsInAState_ShouldLetThroughExactlyTheWritesThatStateAllows(string state, string groups)
    {
        var snapshot = States[state];
        var allowed = groups.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var through = Writes.Where(write => WriteNeeds.StateAllows(write.Build(snapshot), snapshot)).Select(write => write.Name);

        Assert.Equal(Writes.Where(write => allowed.Contains(write.Group)).Select(write => write.Name), through);
    }
}
