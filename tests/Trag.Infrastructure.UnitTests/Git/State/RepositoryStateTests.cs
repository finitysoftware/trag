using Trag.Domain.Naming;
using Trag.Domain.Status;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;
using Trag.TestSupport;
using Trag.Infrastructure.Git.Read;

namespace Trag.Infrastructure.UnitTests.Git.State;

// These three answer from real Git, so they are driven against a throwaway repository rather than a double.
public sealed class RepositoryStateTests
{
    private static readonly (string Name, bool Pattern)[] RefsAskedInOrder =
        [("refs/heads/feature/topic", false), ("refs/heads/broken..name", false), ("refs/heads/*", false), ("refs/heads/*", true), ("refs/heads/" + new string('a', 1024), false)];

    private static readonly string[] BranchNames = ["feature", "feature/topic", "release-1.0", "broken..name", "-leading-dash", "with space", "ends.lock", "trailing/", "", new('a', 256)];

    [Fact]
    public async Task WhenTheBranchIsUnborn_ShouldReadNoHeadButNameTheBranch()
    {
        using var fixture = await GitFixture.CreateAsync();
        var heads = new HeadReader(fixture.Runner);

        var unborn = await heads.ReadAsync(fixture.Root, fixture.Token);
        Assert.Null(unborn.Head);
        Assert.Equal("fixture", unborn.Branch);
    }

    [Fact]
    public async Task WhenTheFolderIsNoRepository_ShouldReportTheExitCodeGitGave()
    {
        using var fixture = await GitFixture.CreateAsync();
        var heads = new HeadReader(fixture.Runner);

        var failure = await Assert.ThrowsAsync<GitCommandException>(() => heads.ReadAsync(fixture.Container, fixture.Token));
        Assert.Equal(128, failure.ExitCode);
    }

    [Fact]
    public async Task WhenTheFirstCommitIsMade_ShouldReadItAsTheHeadOfTheSameBranch()
    {
        using var fixture = await GitFixture.CreateAsync();
        var heads = new HeadReader(fixture.Runner);
        var first = await FirstCommitAsync(fixture, heads);

        var born = await heads.ReadAsync(fixture.Root, fixture.Token);
        Assert.Equal(first, born.Head);
        Assert.Equal("fixture", born.Branch);
    }

    [Fact]
    public async Task WhenTheRootCommitIsRead_ShouldGiveNoParentAndItsTree()
    {
        using var fixture = await GitFixture.CreateAsync();
        var heads = new HeadReader(fixture.Runner);
        var first = await FirstCommitAsync(fixture, heads);
        await heads.ReadAsync(fixture.Root, fixture.Token);

        var root = await heads.ReadCommitAsync(fixture.Root, first, fixture.Token);
        Assert.Equal(string.Empty, root.Parent);
        Assert.Equal(await fixture.GitAsync("rev-parse", first + "^{tree}"), root.Tree);
    }

    [Fact]
    public async Task WhenACommitHasAParent_ShouldReadThatParent()
    {
        using var fixture = await GitFixture.CreateAsync();
        var heads = new HeadReader(fixture.Runner);
        var first = await RootCommitReadAsync(fixture, heads);

        await fixture.WriteAsync("file.txt", "two\n");
        var second = await fixture.CommitAsync("second", "file.txt");
        Assert.Equal(first, (await heads.ReadCommitAsync(fixture.Root, second, fixture.Token)).Parent);
    }

    [Fact]
    public async Task WhenTheHeadIsDetached_ShouldReadItsCommitWithoutABranch()
    {
        using var fixture = await GitFixture.CreateAsync();
        var heads = new HeadReader(fixture.Runner);
        var first = await SecondCommitReadAsync(fixture, heads);

        await fixture.GitAsync("checkout", "--detach", first);
        var detached = await heads.ReadAsync(fixture.Root, fixture.Token);
        Assert.Equal(first, detached.Head);
        Assert.Null(detached.Branch);
    }

    [Fact]
    public async Task WhenGitAcceptsARefName_ShouldAnswerThatItIsUsable()
    {
        using var fixture = await GitFixture.CreateAsync();
        var validator = new GitRefNameValidator(fixture.Runner);

        Assert.True(await validator.IsValidRefAsync(fixture.Root, "refs/heads/feature/topic", false, fixture.Token));
    }

    [Fact]
    public async Task WhenGitRefusesARefName_ShouldAnswerThatItIsNotUsable()
    {
        using var fixture = await GitFixture.CreateAsync();
        var validator = new GitRefNameValidator(fixture.Runner);
        await AskedAboutRefsAsync(fixture, validator, 1);

        Assert.False(await validator.IsValidRefAsync(fixture.Root, "refs/heads/broken..name", false, fixture.Token));
    }

    [Fact]
    public async Task WhenARefNameHoldsAStar_ShouldAcceptItOnlyAsAPattern()
    {
        using var fixture = await GitFixture.CreateAsync();
        var validator = new GitRefNameValidator(fixture.Runner);
        await AskedAboutRefsAsync(fixture, validator, 2);

        Assert.False(await validator.IsValidRefAsync(fixture.Root, "refs/heads/*", false, fixture.Token));
        Assert.True(await validator.IsValidRefAsync(fixture.Root, "refs/heads/*", true, fixture.Token));
    }

    // Refused before Git is asked.
    [Fact]
    public async Task WhenARefNameIsTooLong_ShouldRefuseIt()
    {
        using var fixture = await GitFixture.CreateAsync();
        var validator = new GitRefNameValidator(fixture.Runner);
        await AskedAboutRefsAsync(fixture, validator, 4);

        Assert.False(await validator.IsValidRefAsync(fixture.Root, "refs/heads/" + new string('a', 1024), false, fixture.Token));
    }

    // Refused before Git is asked.
    [Fact]
    public async Task WhenARefNameCarriesAControlCharacter_ShouldRefuseIt()
    {
        using var fixture = await GitFixture.CreateAsync();
        var validator = new GitRefNameValidator(fixture.Runner);
        await AskedAboutRefsAsync(fixture, validator, 5);

        Assert.False(await validator.IsValidRefAsync(fixture.Root, "refs/heads/with\u0001control", false, fixture.Token));
    }

    [Fact]
    public async Task WhenABranchNameIsChecked_ShouldGetTheSameAnswerWhoeverAsks()
    {
        using var fixture = await GitFixture.CreateAsync();
        var validator = new GitRefNameValidator(fixture.Runner);

        foreach (var name in BranchNames)
        {
            var (answer, plain, preset) = await AskEveryWayAsync(fixture, validator, name);
            Assert.Equal(plain.ExitCode == 0, preset.ExitCode == 0);
            if (RefNames.HasSafeCharacters(name, GitLimits.NameLength)) Assert.Equal(plain.ExitCode == 0, answer);
            else Assert.False(answer);
        }
    }

    [Fact]
    public async Task WhenGitAcceptsABranchName_ShouldAnswerThatItIsUsable()
    {
        using var fixture = await GitFixture.CreateAsync();
        var validator = new GitRefNameValidator(fixture.Runner);
        foreach (var name in BranchNames) await AskEveryWayAsync(fixture, validator, name);

        Assert.True(await validator.IsValidBranchNameAsync(fixture.Root, "feature/topic", fixture.Token));
    }

    [Fact]
    public async Task WhenGitRefusesABranchName_ShouldAnswerThatItIsNotUsable()
    {
        using var fixture = await GitFixture.CreateAsync();
        var validator = new GitRefNameValidator(fixture.Runner);
        foreach (var name in BranchNames) await AskEveryWayAsync(fixture, validator, name);
        await validator.IsValidBranchNameAsync(fixture.Root, "feature/topic", fixture.Token);

        Assert.False(await validator.IsValidBranchNameAsync(fixture.Root, "broken..name", fixture.Token));
    }

    // check-ref-format --branch takes @{-1} for the branch checked out before and @{u} for the upstream, and answers with
    // that branch's name; @ it takes as itself, though Git reads it as HEAD wherever it takes a revision, so a branch of
    // that name could never be switched to.
    [Theory]
    [InlineData("@{-1}")]
    [InlineData("@{u}")]
    [InlineData("@")]
    public async Task WhenABranchNameIsGitShorthand_ShouldRefuseIt(string name)
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await fixture.CommitAsync("first");
        await fixture.GitAsync("switch", "-q", "-c", "other");
        await fixture.GitAsync("switch", "-q", "main");
        await fixture.GitAsync("branch", "--set-upstream-to=other");

        Assert.False(await new GitRefNameValidator(fixture.Runner).IsValidBranchNameAsync(fixture.Root, name, fixture.Token));
    }

    [Fact]
    public async Task WhenNothingMovedSinceTheSnapshot_ShouldStillBeExpected()
    {
        using var fixture = await GitFixture.CreateAsync();
        var (snapshot, comparison) = await CommittedAsync(fixture);

        Assert.True(await comparison.StillExpectedAsync(snapshot, fixture.Token));
    }

    [Fact]
    public async Task WhenTheIndexMoves_ShouldNoLongerBeExpected()
    {
        using var fixture = await GitFixture.CreateAsync();
        var (snapshot, comparison) = await CommittedAsync(fixture);
        await comparison.StillExpectedAsync(snapshot, fixture.Token);

        await fixture.WriteAsync("file.txt", "two\n");
        await fixture.GitAsync("add", "--", "file.txt");
        Assert.False(await comparison.StillExpectedAsync(snapshot, fixture.Token));
    }

    [Fact]
    public async Task WhenTheSnapshotIsReadAgainAfterTheIndexMoved_ShouldBeExpectedAgain()
    {
        using var fixture = await GitFixture.CreateAsync();
        var comparison = await StagedAsync(fixture);

        var staged = await fixture.ReadAsync();
        Assert.True(await comparison.StillExpectedAsync(staged, fixture.Token));
    }

    [Fact]
    public async Task WhenHeadMoves_ShouldNoLongerBeExpected()
    {
        using var fixture = await GitFixture.CreateAsync();
        var comparison = await StagedAsync(fixture);
        var staged = await fixture.ReadAsync();
        await comparison.StillExpectedAsync(staged, fixture.Token);

        await fixture.CommitAsync("second");
        Assert.False(await comparison.StillExpectedAsync(staged, fixture.Token));
    }

    // The first commit, made after the reader answered for the unborn branch.
    private static async Task<string> FirstCommitAsync(GitFixture fixture, HeadReader heads)
    {
        await heads.ReadAsync(fixture.Root, fixture.Token);
        await fixture.WriteAsync("file.txt", "one\n");
        return await fixture.CommitAsync("first", "file.txt");
    }

    // ...then read as the head and as a commit, with its tree asked of Git.
    private static async Task<string> RootCommitReadAsync(GitFixture fixture, HeadReader heads)
    {
        var first = await FirstCommitAsync(fixture, heads);
        await heads.ReadAsync(fixture.Root, fixture.Token);
        await heads.ReadCommitAsync(fixture.Root, first, fixture.Token);
        await fixture.GitAsync("rev-parse", first + "^{tree}");
        return first;
    }

    // ...then a second commit on top, read as a commit too. The first commit is what comes back.
    private static async Task<string> SecondCommitReadAsync(GitFixture fixture, HeadReader heads)
    {
        var first = await RootCommitReadAsync(fixture, heads);
        await fixture.WriteAsync("file.txt", "two\n");
        var second = await fixture.CommitAsync("second", "file.txt");
        await heads.ReadCommitAsync(fixture.Root, second, fixture.Token);
        return first;
    }

    // The refs asked about before a later question, in the order they were asked.
    private static async Task AskedAboutRefsAsync(GitFixture fixture, GitRefNameValidator validator, int count)
    {
        foreach (var (name, pattern) in RefsAskedInOrder.Take(count)) await validator.IsValidRefAsync(fixture.Root, name, pattern, fixture.Token);
    }

    // The branch writer used to ask with the write preset and the two sync operations without it.
    // check-ref-format reads no repository, so neither the preset nor its absence may change the answer.
    private static async Task<(bool Answer, GitResult Plain, GitResult Preset)> AskEveryWayAsync(GitFixture fixture, GitRefNameValidator validator, string name)
    {
        var answer = await validator.IsValidBranchNameAsync(fixture.Root, name, fixture.Token);
        var plain = await fixture.Runner.RunAsync(fixture.Root, ["check-ref-format", "--branch", name], fixture.Token);
        var preset = await fixture.Runner.RunAsync(fixture.Root,
            ["-c", "protocol.allow=never", "-c", "core.fsmonitor=false", "check-ref-format", "--branch", name], fixture.Token);
        return (answer, plain, preset);
    }

    // A committed file read into a snapshot, and the comparison that is asked about it.
    private static async Task<(RepositorySnapshot Snapshot, ExpectedStateCheck Comparison)> CommittedAsync(GitFixture fixture)
    {
        await fixture.WriteAsync("file.txt", "one\n");
        await fixture.CommitAsync("first", "file.txt");
        var snapshot = await fixture.ReadAsync();
        var read = new PresetGitRunner(fixture.Runner, LocalGitPreset.Reading);
        var comparison = new ExpectedStateCheck(new WriteStateReader(new HeadReader(fixture.Runner), new GitIdentityReader(read), read));
        return (snapshot, comparison);
    }

    // ...still expected, then a change to the file staged and the old snapshot asked about again.
    private static async Task<ExpectedStateCheck> StagedAsync(GitFixture fixture)
    {
        var (snapshot, comparison) = await CommittedAsync(fixture);
        await comparison.StillExpectedAsync(snapshot, fixture.Token);
        await fixture.WriteAsync("file.txt", "two\n");
        await fixture.GitAsync("add", "--", "file.txt");
        await comparison.StillExpectedAsync(snapshot, fixture.Token);
        return comparison;
    }
}
