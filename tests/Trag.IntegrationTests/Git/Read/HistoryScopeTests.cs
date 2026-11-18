using System.Text;
using Trag.Application.Workspace;
using Trag.Domain.History;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// How far a page reaches (the current branch with its upstream, or every branch), what the snapshot knows of the
// local refs a commit or a branch write moves and of the remote-tracking refs a fetch moves, and how much of one
// commit the details will hold.
public sealed class HistoryScopeTests
{
    // A release tagged on a branch that was deleted afterwards, a tag fetched for a commit no branch has: only the tag
    // reaches the commit. "All branches" is where the user looks for it, and the tag list's Show searches there too.
    [Fact]
    public async Task WhenACommitIsReachableOnlyThroughATag_ShouldStillBeInTheAllBranchesHistory()
    {
        using var f = await GitFixture.CreateAsync();
        await f.GitAsync("commit", "--allow-empty", "-m", "Shared");
        await f.GitAsync("switch", "-c", "release");
        await f.GitAsync("commit", "--allow-empty", "-m", "Release 1.0");
        var release = await Text(f, "rev-parse", "HEAD");
        await f.GitAsync("tag", "-a", "-m", "Version 1.0", "v1.0");
        await f.GitAsync("switch", "fixture");
        await f.GitAsync("branch", "-D", "release");
        var head = await Text(f, "rev-parse", "HEAD");
        var reader = new CliHistoryReader(new());

        var all = await reader.ReadPageAsync(f.Root, head, new("", 0, AllBranches: true), f.Token);
        var found = await reader.ReadPageAsync(f.Root, head, new(release, 0, AllBranches: true), f.Token);

        Assert.Contains(release, all.Commits.Select(commit => commit.Id));
        Assert.Contains(release, found.Commits.Select(commit => commit.Id));
    }

    // A tag may point at a tree or a blob (git.git's own junio-gpg-pub is one); such a tag reaches no commit, and the
    // history of every branch is read as if it were not there.
    [Fact]
    public async Task WhenATagPointsAtATreeOrABlob_ShouldStillReadTheAllBranchesHistory()
    {
        using var f = await GitFixture.CreateAsync();
        await f.WriteAsync("key.txt", "public key\n");
        await f.CommitAsync("Shared");
        await f.GitAsync("tag", "tree-tag", "HEAD^{tree}");
        await f.GitAsync("tag", "blob-tag", "HEAD:key.txt");
        var head = await Text(f, "rev-parse", "HEAD");

        var all = await new CliHistoryReader(new()).ReadPageAsync(f.Root, head, new("", 0, AllBranches: true), f.Token);

        Assert.Equal([head], all.Commits.Select(commit => commit.Id));
    }

    // An orphan branch before its first commit leaves HEAD unborn, while the other branches keep their commits: "All
    // branches" lists them, and the current branch has nothing to list.
    [Fact]
    public async Task WhenHeadIsAnOrphanBranchAndOtherBranchesHaveCommits_ShouldListThemInAllBranches()
    {
        using var f = await GitFixture.CreateAsync();
        await f.GitAsync("commit", "--allow-empty", "-m", "On fixture");
        var commit = await Text(f, "rev-parse", "HEAD");
        await f.GitAsync("switch", "--orphan", "lonely");
        await using var workspace = new RepositoryWorkspace(new CliRepositoryReader(f.Runner), historyReader: new CliHistoryReader(f.Runner));
        var opened = (await workspace.OpenAsync(f.Root, f.Token))!;

        var all = await workspace.ReadHistoryAsync(opened, new("", 0, AllBranches: true), f.Token);
        var current = await workspace.ReadHistoryAsync(opened, new("", 0), f.Token);

        Assert.Equal([commit], all!.Commits.Select(found => found.Id));
        Assert.Empty(current!.Commits);
    }

    [Fact]
    public async Task WhenAllBranchesAreAsked_ShouldWalkTheBranchesTheHeadCannotReach()
    {
        using var f = await GitFixture.CreateAsync();
        await f.GitAsync("commit", "--allow-empty", "-m", "Shared");
        await f.GitAsync("switch", "-c", "topic");
        await f.GitAsync("commit", "--allow-empty", "-m", "Only on topic");
        var topic = await Text(f, "rev-parse", "HEAD");
        await f.GitAsync("switch", "fixture");
        await f.GitAsync("commit", "--allow-empty", "-m", "Only on fixture");
        var head = await Text(f, "rev-parse", "HEAD");
        var reader = new CliHistoryReader(new());

        var current = await reader.ReadPageAsync(f.Root, head, new("", 0), f.Token);
        Assert.DoesNotContain(topic, current.Commits.Select(commit => commit.Id));
        var all = await reader.ReadPageAsync(f.Root, head, new("", 0, AllBranches: true), f.Token);
        Assert.Contains(topic, all.Commits.Select(commit => commit.Id));
        Assert.Contains(head, all.Commits.Select(commit => commit.Id));
        Assert.Equal(3, all.Commits.Count);
    }

    [Fact]
    public async Task WhenAFetchMovesTheUpstreamAboveHead_ShouldReportTheNewUpstreamHeadAsAChangeOfRemotesNotOfHistory()
    {
        using var f = await GitFixture.CreateAsync();
        var head = await TrackingAtHeadAsync(f);
        var before = await f.ReadAsync();
        Assert.Equal(head, before.UpstreamHead); Assert.NotEqual("", before.RemoteRefs);

        var pushed = await FetchAboveAsync(f, head);
        var after = await f.ReadAsync();
        Assert.Equal((head, pushed), (after.Head, after.UpstreamHead));
        Assert.False(after.HasSameRemotes(before)); Assert.True(after.HasSameHistory(before));
        Assert.Equal(before.LocalRefs, after.LocalRefs); Assert.NotEqual(before.BranchesKey, after.BranchesKey);
    }

    [Fact]
    public async Task WhenACommitOrABranchWriteMovesALocalRef_ShouldReportItAsAChangeOfTheBranchesNotOfTheRemotes()
    {
        using var f = await GitFixture.CreateAsync();
        await TrackingAtHeadAsync(f);
        var before = await f.ReadAsync();
        Assert.NotEqual("", before.LocalRefs);

        await f.GitAsync("branch", "topic");
        var branched = await f.ReadAsync();
        Assert.NotEqual(before.LocalRefs, branched.LocalRefs); Assert.True(branched.HasSameRemotes(before));
        Assert.NotEqual(before.BranchesKey, branched.BranchesKey);

        await f.GitAsync("commit", "--allow-empty", "-m", "Moves fixture");
        var committed = await f.ReadAsync();
        Assert.NotEqual(branched.LocalRefs, committed.LocalRefs); Assert.True(committed.HasSameRemotes(branched));

        await f.GitAsync("branch", "-D", "topic");
        var deleted = await f.ReadAsync();
        Assert.NotEqual(committed.LocalRefs, deleted.LocalRefs); Assert.True(deleted.HasSameRemotes(committed));
        Assert.Equal(before.LocalRefs.Length, deleted.LocalRefs.Length);
    }

    [Fact]
    public async Task WhenTheUpstreamIsAheadOfHead_ShouldShowItsCommitsAboveHeadOnlyOnAPageGivenTheUpstream()
    {
        using var f = await GitFixture.CreateAsync();
        var head = await TrackingAtHeadAsync(f);
        var pushed = await FetchAboveAsync(f, head);

        var reader = new CliHistoryReader(new());
        Assert.DoesNotContain(pushed, (await reader.ReadPageAsync(f.Root, head, new("", 0), f.Token)).Commits.Select(commit => commit.Id));
        var withUpstream = await reader.ReadPageAsync(f.Root, head, new("", 0) { Upstream = pushed }, f.Token);
        Assert.Equal([pushed, head], withUpstream.Commits.Select(commit => commit.Id));
    }

    [Fact]
    public async Task WhenACommitHasMoreFilesThanTheReaderHolds_ShouldCutTheListAndSaySo()
    {
        using var f = await GitFixture.CreateAsync(scenarioTimeout: TimeSpan.FromSeconds(240));
        await ImportFiles(f, HistoryLimits.FileLimit + 1);
        var head = await Text(f, "rev-parse", "HEAD");
        var details = await new CliHistoryReader(new()).ReadCommitAsync(f.Root, head, f.Token);
        Assert.Equal(HistoryLimits.FileLimit, details.Files.Count);
        Assert.Equal("Showing up to 10,000 changed files. This commit may contain more files.", details.FileNotice);
    }

    // One commit on fixture, which tracks origin/fixture at that same commit.
    private static async Task<string> TrackingAtHeadAsync(GitFixture f)
    {
        await f.GitAsync("commit", "--allow-empty", "-m", "Shared");
        var head = await Text(f, "rev-parse", "HEAD");
        await f.GitAsync("config", "remote.origin.url", "https://fetch.example.invalid/origin");
        await f.GitAsync("config", "remote.origin.fetch", "+refs/heads/*:refs/remotes/origin/*");
        await f.GitAsync("config", "branch.fixture.remote", "origin");
        await f.GitAsync("config", "branch.fixture.merge", "refs/heads/fixture");
        await f.GitAsync("update-ref", "refs/remotes/origin/fixture", head);
        return head;
    }

    // What a fetch does: someone else's commit arrives on the remote-tracking ref, HEAD stays.
    private static async Task<string> FetchAboveAsync(GitFixture f, string head)
    {
        var pushed = await Text(f, "commit-tree", head + "^{tree}", "-p", head, "-m", "Pushed by someone else");
        await f.GitAsync("update-ref", "refs/remotes/origin/fixture", pushed);
        return pushed;
    }

    private static async Task<string> Text(GitFixture f, params string[] arguments)
    {
        var result = await f.RunAsync(arguments);
        Assert.Equal(0, result.ExitCode);
        return Encoding.UTF8.GetString(result.StandardOutput).Trim();
    }

    // One commit with more files than the reader will ever return, written straight into the object database.
    private static async Task ImportFiles(GitFixture f, int count)
    {
        const string message = "Many files";
        var input = new StringBuilder();
        input.Append("commit refs/heads/fixture\ncommitter Fixture <fixture@example.invalid> 1700000000 +0000\n")
            .Append("data ").Append(message.Length).Append('\n').Append(message).Append('\n');
        for (var i = 0; i < count; i++) input.Append($"M 100644 inline file-{i:D5}.txt\ndata 2\n{i % 10}\n");
        input.Append("\ndone\n");
        var result = await new GitProcessRunner().RunAsync(f.Root, ["fast-import", "--quiet"], f.Token, standardInput: Encoding.UTF8.GetBytes(input.ToString()));
        Assert.True(result.ExitCode == 0, Encoding.UTF8.GetString(result.StandardError));
    }
}
