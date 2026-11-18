using Trag.Domain;
using Trag.Domain.History;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Sync;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Shapes;

// A clone made with --depth 2 holds the two newest commits of each branch. Git lists the oldest of them in .git/shallow:
// each still names its parent, which is not here, and Git reads it as a commit without one. The origin's main is One,
// Two, Three and Four, and its topic branched off at Two with Topic one and Topic two, so the clone has Three and Four of
// main and both topic commits, and Two, the commit main and topic share, stays behind the boundary.
public sealed class ShallowCloneTests
{
    private static async Task<(string Bare, string Shallow)> ShallowCloneAsync(GitFixture f)
    {
        await f.WriteAsync("f.txt", "one\n"); await f.CommitAsync("One");
        await f.WriteAsync("f.txt", "one\ntwo\n"); await f.CommitAsync("Two");
        await f.GitAsync("switch", "-c", "topic");
        await f.WriteAsync("t.txt", "topic one\n"); await f.CommitAsync("Topic one");
        await f.WriteAsync("t.txt", "topic one\ntopic two\n"); await f.CommitAsync("Topic two");
        await f.GitAsync("switch", "main");
        await f.WriteAsync("f.txt", "one\ntwo\nthree\n"); await f.CommitAsync("Three");
        await f.WriteAsync("f.txt", "one\ntwo\nthree\nfour\n"); await f.CommitAsync("Four");
        var bare = Path.Combine(f.Container, "origin.git");
        await f.GitAtAsync(f.Container, "init", "--bare", "--initial-branch=main", bare);
        await f.GitAsync("push", bare, "main", "topic");
        // A plain local path ignores --depth; only a file:// address makes the clone shallow.
        var shallow = Path.Combine(f.Container, "shallow");
        await f.GitAtAsync(f.Container, "clone", "--depth", "2", "--no-single-branch", "--", new Uri(bare).AbsoluteUri, shallow);
        await f.GitAtAsync(shallow, "config", "user.name", "Trag Fixture");
        await f.GitAtAsync(shallow, "config", "user.email", "fixture@example.invalid");
        await f.GitAtAsync(shallow, "config", "commit.gpgsign", "false");
        await f.GitAtAsync(shallow, "config", "core.hooksPath", Path.Combine(f.Container, "empty-hooks"));
        return (bare, shallow);
    }

    private static async Task<HistoryPage> ReadHistoryAsync(GitFixture f, string shallow) =>
        await new CliHistoryReader(f.Runner).ReadPageAsync(shallow, await f.GitAtAsync(shallow, "rev-parse", "HEAD"), new(), f.Token);

    // Git decorates the oldest commit "grafted", which is no branch or tag.
    [Fact]
    public async Task WhenTheHistoryReachesTheShallowBoundary_ShouldEndThereWithoutMoreToRead()
    {
        using var f = await GitFixture.CreateAsync("main");
        var (_, shallow) = await ShallowCloneAsync(f);

        var page = await ReadHistoryAsync(f, shallow);

        Assert.Equal(["Four", "Three"], page.Commits.Select(commit => commit.Subject));
        Assert.False(page.HasMore);
        Assert.Empty(page.Commits[^1].Refs);
    }

    // The parent the oldest commit names is not in the clone: the commit reads as having none, and its line ends there
    // instead of running on below a page that has nothing more.
    [Fact]
    public async Task WhenTheOldestVisibleCommitsParentIsNotInTheClone_ShouldEndItsGraphLineThere()
    {
        using var f = await GitFixture.CreateAsync("main");
        var (_, shallow) = await ShallowCloneAsync(f);
        var commits = (await ReadHistoryAsync(f, shallow)).Commits;

        var graph = CommitGraphLayout.Build(commits);

        Assert.Empty(commits[^1].Parents);
        Assert.Empty(graph.For(commits[^1].Id)!.Outgoing);
    }

    // Git shows the oldest commit of the clone without a parent and compares it with an empty tree; the commit pane says its
    // parent is not here, while the newest commit is compared with its parent and the origin's first commit stays a root commit.
    [Fact]
    public async Task WhenTheOldestCommitOfTheCloneIsShown_ShouldSayItsParentIsNotHereInsteadOfCallingItARootCommit()
    {
        using var f = await GitFixture.CreateAsync("main");
        var (_, shallow) = await ShallowCloneAsync(f);
        var reader = new CliHistoryReader(f.Runner);
        var (newestId, oldestId) = (await f.GitAtAsync(shallow, "rev-parse", "HEAD"), await f.GitAtAsync(shallow, "rev-parse", "HEAD~1"));

        var oldest = await reader.ReadCommitAsync(shallow, oldestId, f.Token);
        var newest = await reader.ReadCommitAsync(shallow, newestId, f.Token);
        var first = await reader.ReadCommitAsync(f.Root, await f.GitAsync("rev-list", "--max-parents=0", "main"), f.Token);

        Assert.Equal(("Oldest commit of this shallow clone · its parent is not here, so it is compared with an empty tree", "Compared with parent " + oldestId,
            "Root commit · compared with an empty tree"), (CommitText.Comparison(oldest), CommitText.Comparison(newest), CommitText.Comparison(first)));
    }

    // An edit from the oldest commit would run from the root, where Git writes the commits it changes without the parent the
    // oldest one names, and main would share no commit with origin/main. A newer commit has its parent here.
    [Fact]
    public async Task WhenCommitsAreEditedFromTheOldestVisibleCommit_ShouldRefuseInsteadOfCuttingTheBranchFromItsHistory()
    {
        using var f = await GitFixture.CreateAsync("main");
        var (_, shallow) = await ShallowCloneAsync(f);
        var commits = (await ReadHistoryAsync(f, shallow)).Commits;
        var (newest, oldest) = (commits[0], commits[^1]);
        var snapshot = await f.ReadAsync(shallow);
        var reader = new CliBranchReader(f.Runner);

        var refusal = CommitRefusals.Rewrite(oldest, snapshot, await reader.ReadRewriteAsync(shallow, oldest.Id, f.Token));

        Assert.Equal(RewriteText.ShallowStart(GitObjectId.Short(oldest.Id)), refusal);
        Assert.Equal("", CommitRefusals.Rewrite(newest, snapshot, await reader.ReadRewriteAsync(shallow, newest.Id, f.Token)));
    }

    // On the oldest commit of the clone Git would write the amended commit without the parent the oldest one names, and the
    // branch would share no commit with its upstream's history; the amend is refused and the commit stays.
    [Fact]
    public async Task WhenTheLastCommitIsTheOldestOfTheShallowClone_ShouldRefuseToAmendItInsteadOfCuttingTheBranchFromItsHistory()
    {
        using var f = await GitFixture.CreateAsync("main");
        var (_, shallow) = await ShallowCloneAsync(f);
        await f.GitAtAsync(shallow, "switch", "-c", "edge", "HEAD~1");
        var oldest = await f.GitAtAsync(shallow, "rev-parse", "HEAD");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new CommitRequest(await f.ReadAsync(shallow), "Three, amended", Amend: true), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, CommitOutcomeText.ShallowAmend), (result.Outcome, result.Message));
        Assert.Equal(oldest, await f.GitAtAsync(shallow, "rev-parse", "HEAD"));
    }

    // Git reads the oldest commit of the clone as a first commit: reverting it would delete every file it holds, and picking
    // it would add them all, instead of undoing or applying its own change.
    [Fact]
    public async Task WhenTheOldestCommitOfTheShallowCloneIsReverted_ShouldRefuseInsteadOfUndoingEveryFileItHolds()
    {
        using var f = await GitFixture.CreateAsync("main");
        var (_, shallow) = await ShallowCloneAsync(f);
        var (head, oldest) = (await f.GitAtAsync(shallow, "rev-parse", "HEAD"), await f.GitAtAsync(shallow, "rev-parse", "HEAD~1"));

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new RevertRequest(await f.ReadAsync(shallow), oldest), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, PickText.ShallowBoundary(RepositoryOperation.Revert, GitObjectId.Short(oldest))), (result.Outcome, result.Message));
        Assert.Equal(head, await f.GitAtAsync(shallow, "rev-parse", "HEAD"));
    }

    [Fact]
    public async Task WhenTheOldestCommitOfTheShallowCloneIsCherryPicked_ShouldRefuseInsteadOfApplyingEveryFileItHolds()
    {
        using var f = await GitFixture.CreateAsync("main");
        var (_, shallow) = await ShallowCloneAsync(f);
        var oldest = await f.GitAtAsync(shallow, "rev-parse", "HEAD~1");
        await f.GitAtAsync(shallow, "switch", "-c", "topic", "origin/topic");
        var head = await f.GitAtAsync(shallow, "rev-parse", "HEAD");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new CherryPickRequest(await f.ReadAsync(shallow), oldest), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, PickText.ShallowBoundary(RepositoryOperation.CherryPick, GitObjectId.Short(oldest))), (result.Outcome, result.Message));
        Assert.Equal((head, RepositoryOperation.None), (await f.GitAtAsync(shallow, "rev-parse", "HEAD"), (await f.ReadAsync(shallow)).Operation));
    }

    // main and origin/topic share Two, which the clone does not have: here they share no commit, as if they had started
    // apart, and merge-tree cannot check them.
    [Fact]
    public async Task WhenTheCommonCommitIsBeyondTheShallowBoundary_ShouldSayTheBranchesShareNoCommitHere()
    {
        using var f = await GitFixture.CreateAsync("main");
        var (_, shallow) = await ShallowCloneAsync(f);

        var preview = await new CliBranchReader(f.Runner).PreviewMergeAsync(shallow, "refs/heads/main", "refs/remotes/origin/topic", f.Token);

        Assert.Equal((2, 2), (preview.IncomingCount, preview.OwnCount));
        Assert.Null(preview.Conflicts);
        Assert.True(preview.SharesNoCommit);
    }

    [Fact]
    public async Task WhenABranchIsPushedFromAShallowClone_ShouldRefuseWithTheShallowSentenceAndLeaveTheRemoteAsItWas()
    {
        using var f = await GitFixture.CreateAsync("main");
        var (bare, shallow) = await ShallowCloneAsync(f);
        await File.WriteAllTextAsync(Path.Combine(shallow, "f.txt"), "one\ntwo\nthree\nfour\nfive\n", f.Token);
        await f.GitAtAsync(shallow, "commit", "--all", "-m", "Five");
        var remote = await f.GitAtAsync(bare, "rev-parse", "refs/heads/main");
        var sync = new GitSyncFacade(f.Runner, new CliRepositoryReader(f.Runner));
        var snapshot = await f.ReadAsync(shallow);
        var state = await sync.ReadAsync(snapshot, "origin", null, f.Token);

        var result = await sync.ExecuteAsync(new SyncRequest(snapshot, state, SyncAction.Push), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, SyncOutcomeText.Shallow), (result.Outcome, result.Message));
        Assert.Equal(remote, await f.GitAtAsync(bare, "rev-parse", "refs/heads/main"));
        Assert.Equal(((int?)1, SyncOutcomeText.Shallow), (state.Ahead, state.Remote.Limitation));
    }
}
