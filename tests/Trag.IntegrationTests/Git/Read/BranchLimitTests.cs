using System.Text;
using Trag.Domain.Naming;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// The branch reader answers from one for-each-ref call: the count is bounded and a symbolic ref such as origin/HEAD
// is an alias, not a branch.
public sealed class BranchLimitTests
{
    [Fact]
    public async Task WhenThereAreMoreBranchesThanTheLimit_ShouldReturnTheLimitAndSayTheListIsTruncated()
    {
        using var fixture = await GitFixture.CreateAsync(scenarioTimeout: TimeSpan.FromMinutes(3));
        await fixture.WriteAsync("file.txt", "content\n");
        var head = await fixture.CommitAsync("first", "file.txt");
        await CreateRefsAsync(fixture, head, "refs/heads/many-", GitLimits.BranchCount + 1);

        var branches = await new CliBranchReader(fixture.Runner).ReadBranchesAsync(fixture.Root, fixture.Token);

        Assert.True(branches.Truncated);
        Assert.Equal(GitLimits.BranchCount, branches.Local.Count + branches.Remote.Count);
        Assert.Equal("fixture", branches.Current);
    }

    // origin/HEAD is no branch, so it takes no place of one under the limit.
    [Fact]
    public async Task WhenThereAreFiveThousandBranchesAndARemoteHead_ShouldListAllFiveThousand()
    {
        using var fixture = await GitFixture.CreateAsync(scenarioTimeout: TimeSpan.FromMinutes(3));
        await fixture.WriteAsync("file.txt", "content\n");
        var head = await fixture.CommitAsync("first", "file.txt");
        await CreateRefsAsync(fixture, head, "refs/heads/many-", GitLimits.BranchCount - 2);
        await fixture.GitAsync("update-ref", "refs/remotes/origin/main", head);
        await fixture.GitAsync("symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/main");

        var branches = await new CliBranchReader(fixture.Runner).ReadBranchesAsync(fixture.Root, fixture.Token);

        Assert.False(branches.Truncated);
        Assert.Equal(GitLimits.BranchCount, branches.Local.Count + branches.Remote.Count);
        Assert.Equal("origin/main", Assert.Single(branches.Remote).Name);
    }

    // Nor does it hide the one branch past the limit.
    [Fact]
    public async Task WhenThereAreMoreBranchesThanTheLimitAndARemoteHead_ShouldSayTheListIsTruncated()
    {
        using var fixture = await GitFixture.CreateAsync(scenarioTimeout: TimeSpan.FromMinutes(3));
        await fixture.WriteAsync("file.txt", "content\n");
        var head = await fixture.CommitAsync("first", "file.txt");
        await CreateRefsAsync(fixture, head, "refs/heads/many-", GitLimits.BranchCount - 1);
        await fixture.GitAsync("update-ref", "refs/remotes/origin/main", head);
        await fixture.GitAsync("symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/main");

        var branches = await new CliBranchReader(fixture.Runner).ReadBranchesAsync(fixture.Root, fixture.Token);

        Assert.True(branches.Truncated);
        Assert.Equal(GitLimits.BranchCount, branches.Local.Count + branches.Remote.Count);
    }

    // The current branch is listed first, so one that sorts past the limit is still the current branch of the list.
    [Fact]
    public async Task WhenTheCurrentBranchSortsAfterTheLimit_ShouldStillBeShownAsCurrent()
    {
        using var fixture = await GitFixture.CreateAsync(scenarioTimeout: TimeSpan.FromMinutes(3));
        await fixture.WriteAsync("file.txt", "content\n");
        var head = await fixture.CommitAsync("first", "file.txt");
        await CreateRefsAsync(fixture, head, "refs/heads/many-", GitLimits.BranchCount + 1);
        await fixture.GitAsync("switch", "-c", "zz-current");

        var branches = await new CliBranchReader(fixture.Runner).ReadBranchesAsync(fixture.Root, fixture.Token);

        Assert.True(branches.Truncated);
        Assert.Equal("zz-current", branches.Current);
        Assert.Equal(head, Assert.Single(branches.Local, branch => branch.Name == "zz-current").Head);
    }

    [Fact]
    public async Task WhenBranchesAreListed_ShouldNameTheCurrentOneFromTheSameGitCall()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("file.txt", "content\n"); await fixture.CommitAsync("first", "file.txt");
        await fixture.GitAsync("branch", "other");
        var calls = 0;
        var reader = new CliBranchReader(new Trag.Infrastructure.Git.Processes.GitProcessRunner(exited: _ => Interlocked.Increment(ref calls)));

        var branches = await reader.ReadBranchesAsync(fixture.Root, fixture.Token);
        Assert.Equal(("fixture", 1), (branches.Current, calls));
        await fixture.GitAsync("switch", "--detach");
        Assert.Null((await reader.ReadBranchesAsync(fixture.Root, fixture.Token)).Current);
    }

    // A remote branch is named on its remote through the fetch mappings, read once for the whole list: 5,000 local and
    // remote branches cost the two Git processes one of each does (1.4 to 2.6 s here under load), where reading the
    // mappings for each remote branch outran the reader's 30 seconds.
    [Fact]
    public async Task WhenFiveThousandLocalAndRemoteBranchesAreListed_ShouldReadThemWithTwoGitProcesses()
    {
        using var fixture = await GitFixture.CreateAsync(scenarioTimeout: TimeSpan.FromMinutes(3));
        await fixture.WriteAsync("file.txt", "content\n");
        var head = await fixture.CommitAsync("first", "file.txt");
        await fixture.GitAsync("remote", "add", "origin", "https://example.invalid/origin.git");
        await CreateRefsAsync(fixture, head, "refs/heads/many-", 2_500);
        await CreateRefsAsync(fixture, head, "refs/remotes/origin/many-", 2_499);
        var calls = 0;
        var reader = new CliBranchReader(new Trag.Infrastructure.Git.Processes.GitProcessRunner(exited: _ => Interlocked.Increment(ref calls)));

        var branches = await reader.ReadBranchesAsync(fixture.Root, fixture.Token);

        Assert.Equal((2_501, 2_499, false, 2), (branches.Local.Count, branches.Remote.Count, branches.Truncated, calls));
    }

    [Fact]
    public async Task WhenTheRemoteHasAHeadAlias_ShouldNotOfferItAsABranch()
    {
        using var fixture = await GitFixture.CreateAsync(scenarioTimeout: TimeSpan.FromMinutes(3));
        await fixture.WriteAsync("file.txt", "content\n");
        var head = await fixture.CommitAsync("first", "file.txt");
        await fixture.GitAsync("update-ref", "refs/remotes/origin/main", head);
        await fixture.GitAsync("symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/main");

        var branches = await new CliBranchReader(fixture.Runner).ReadBranchesAsync(fixture.Root, fixture.Token);

        Assert.False(branches.Truncated);
        Assert.Equal("origin/main", Assert.Single(branches.Remote).Name);
        Assert.Equal(head, Assert.Single(branches.Remote).Head);
        Assert.Equal("fixture", Assert.Single(branches.Local).Name);
    }

    // The refs go into Git's packed-refs file, as a clone leaves them. Thousands of loose ref files written and read at
    // once can take Windows longer than the reader's 30 seconds to open the first time.
    private static Task CreateRefsAsync(GitFixture fixture, string head, string prefix, int count)
    {
        var packed = new StringBuilder(count * 64);
        for (var i = 0; i < count; i++) packed.Append(head).Append(' ').Append(prefix).Append(i.ToString("D5")).Append('\n');
        return File.AppendAllTextAsync(Path.Combine(fixture.Root, ".git", "packed-refs"), packed.ToString(), fixture.Token);
    }
}
