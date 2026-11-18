using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// An untracked file ignored from its menu, by a line added to the repository's .gitignore with real Git: the file made
// when there is none, a line appended in the file's own line endings, and Git asked whether it now ignores the file.
public sealed class IgnoreTests
{
    private static async Task<GitFixture> UntrackedAsync()
    {
        var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("a.txt", "a\n");
        await f.CommitAsync("one", "a.txt");
        await f.WriteAsync("out.log", "x\n");
        return f;
    }

    private static Task<GitWriteResult> IgnoreAsync(GitFixture f, RepositorySnapshot before, IgnoreChoice choice) =>
        new GitWriteRouter(f.Runner).ExecuteAsync(new IgnoreRequest(before, choice.Path, choice.Pattern), f.Token);

    private static string Ignored(GitFixture f) => File.ReadAllText(Path.Combine(f.Root, ".gitignore"));

    // "[a].txt" read as a glob is the tracked a.txt, and a tracked file is never ignored: the untracked file of that name
    // is ignored all the same.
    [Fact]
    public async Task WhenTheNameReadAsAGlobIsATrackedFile_ShouldStillIgnoreTheUntrackedFile()
    {
        using var f = await UntrackedAsync();
        await f.WriteAsync("[a].txt", "hostile\n");
        var choice = IgnoreChoices.File(Snapshots.Path("[a].txt"))!;

        var result = await IgnoreAsync(f, await f.ReadAsync(), choice);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.DoesNotContain((await f.ReadAsync()).Files, file => file.Path.Display == "[a].txt");
    }

    // Git does not read a .gitignore that is a link, and a line appended through one lands outside the repository: the
    // ignore is refused and the file the link leads to stays as it was. Windows makes a file link only for an
    // administrator or in Developer Mode; without either the case cannot be set up and the test has nothing to check.
    [Fact]
    public async Task WhenGitignoreIsALinkOutsideTheRepository_ShouldRefuseAndWriteNothingThere()
    {
        using var f = await UntrackedAsync();
        var outside = Path.Combine(f.Container, "outside.txt");
        await File.WriteAllTextAsync(outside, "outside\n", f.Token);
        try { File.CreateSymbolicLink(Path.Combine(f.Root, ".gitignore"), outside); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }

        var result = await IgnoreAsync(f, await f.ReadAsync(), IgnoreChoices.File(Snapshots.Path("out.log"))!);

        Assert.Equal((GitWriteOutcome.Rejected, IgnoreText.GitignoreIsLink), (result.Outcome, result.Message));
        Assert.Equal("outside\n", await File.ReadAllTextAsync(outside, f.Token));
    }

    [Fact]
    public async Task WhenAnUntrackedFileIsIgnored_ShouldWriteItsLineAndLeaveItOutOfTheStatus()
    {
        using var f = await UntrackedAsync();
        var choice = IgnoreChoices.File(Snapshots.Path("out.log"))!;

        var result = await IgnoreAsync(f, await f.ReadAsync(), choice);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(IgnoreText.Ignored("/out.log"), result.Message);
        Assert.Equal("/out.log\n", Ignored(f));
        Assert.DoesNotContain((await f.ReadAsync()).Files, file => file.Path.Display == "out.log");
    }

    // The line Trag writes for the name ignores that file alone: x.txt, untracked beside it, is what "[x].txt" would match
    // as a glob. Git lists a name it cannot index as untracked, so such a name is ignored like any other. A name with a
    // line break is offered no line, so off Windows that row has nothing to ignore.
    [Theory]
    [MemberData(nameof(HostileNameRows.Untracked), MemberType = typeof(HostileNameRows))]
    public async Task WhenAnUntrackedFileHasAHostileName_ShouldIgnoreItByItsOwnLineAndNoOtherFile(string label)
    {
        var name = TestNames.Find(label).Path;
        var choice = IgnoreChoices.File(Snapshots.Path(name));
        if (!OperatingSystem.IsWindows() && choice is null) return;
        Assert.NotNull(choice);
        using var f = await UntrackedAsync();
        await f.WriteAsync(name, "hostile\n");
        await f.WriteAsync("x.txt", "decoy\n");

        var result = await IgnoreAsync(f, await f.ReadAsync(), choice);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(choice.Pattern + "\n", Ignored(f));
        Assert.Equal([".gitignore", "out.log", "x.txt"], (await f.ReadAsync()).Files.Select(file => file.Path.Display).Order(StringComparer.Ordinal));
    }

    // A .gitignore in the file's folder lets it in again: the line is added at the end of the root one, Git still lists
    // the file, and the result says both.
    [Fact]
    public async Task WhenAFoldersGitignoreLetsTheFileIn_ShouldSayTheLineWasAddedAndTheFileIsStillListed()
    {
        using var f = await UntrackedAsync();
        await f.WriteAsync("logs/out.log", "x\n");
        await f.WriteAsync("logs/.gitignore", "!out.log\n");

        var result = await IgnoreAsync(f, await f.ReadAsync(), IgnoreChoices.Extension(Snapshots.Path("logs/out.log"))!);

        Assert.Equal((GitWriteOutcome.Rejected, IgnoreText.AddedButListed("*.log")), (result.Outcome, result.Message));
        Assert.Equal("*.log\n", Ignored(f));
    }

    // A later line lets the file in again, so the line already there, CRLF and all, is not written twice and Git still
    // lists the file.
    [Fact]
    public async Task WhenALaterLineLetsTheFileIn_ShouldWriteNothingTwiceAndSayItIsStillNotIgnored()
    {
        using var f = await UntrackedAsync();
        await File.WriteAllTextAsync(Path.Combine(f.Root, ".gitignore"), "*.log\r\n!out.log\r\n", f.Token);

        var result = await IgnoreAsync(f, await f.ReadAsync(), IgnoreChoices.Extension(Snapshots.Path("out.log"))!);

        Assert.Equal((GitWriteOutcome.Rejected, IgnoreText.NotIgnored), (result.Outcome, result.Message));
        Assert.Equal("*.log\r\n!out.log\r\n", Ignored(f));
    }
}
