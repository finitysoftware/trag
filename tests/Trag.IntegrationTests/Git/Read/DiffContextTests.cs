using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// A diff shows the whole file around its changes, in the working tree and in a commit alike: two changes far apart
// arrive as one hunk with every line between them, never as two hunks with lines left out.
public sealed class DiffContextTests
{
    [Fact]
    public async Task WhenTwoChangesAreFarApart_ShouldReadTheWholeFileAsOneHunkInTheWorkingTreeAndInTheCommit()
    {
        using var fixture = await GitFixture.CreateAsync();
        // The diffs are compared header and all; the headers' path prefixes are the user's to choose, and no row shows them.
        await fixture.GitAsync("config", "diff.noprefix", "false"); await fixture.GitAsync("config", "diff.mnemonicPrefix", "false");
        var lines = Enumerable.Range(1, 40).Select(number => "line " + number).ToArray();
        await fixture.WriteAsync("sample.txt", string.Join("\n", lines) + "\n");
        await fixture.CommitAsync("Before", "sample.txt");
        lines[1] = "changed 2"; lines[35] = "changed 36";
        await fixture.WriteAsync("sample.txt", string.Join("\n", lines) + "\n");

        var work = await new CliFileDiffReader(new()).ReadAsync(fixture.Root, Assert.Single((await fixture.ReadAsync()).Files), false, false, fixture.Token);

        Assert.Single(work.Lines, line => line.IsHeader && line.Text.StartsWith("@@", StringComparison.Ordinal));
        Assert.Equal(40, work.Lines.Count(line => line.Kind is ' ' or '+'));
        await fixture.CommitAsync("After", "sample.txt");
        var history = new CliHistoryReader(new());
        var details = await history.ReadCommitAsync(fixture.Root, (await fixture.ReadAsync()).Head!, fixture.Token);
        var committed = await history.ReadDiffAsync(fixture.Root, details, Assert.Single(details.Files), false, fixture.Token);
        Assert.Equal(work.Lines, committed.Lines);
    }

    // diff.suppressBlankEmpty prints an unchanged blank line as an empty one. Every line of the file is still there, in the
    // working tree's diff and in the commit's, and the last one keeps its number.
    [Fact]
    public async Task WhenGitSuppressesTheSpaceOfBlankContextLines_ShouldStillShowEveryLineInTheWorkingTreeAndInTheCommit()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "diff.suppressBlankEmpty", "true");
        await fixture.WriteAsync("sample.txt", "title\n\nfirst\n\nsecond\n\nlast\n");
        await fixture.CommitAsync("Before", "sample.txt");
        await fixture.WriteAsync("sample.txt", "title\n\nfirst\n\nsecond\n\nLAST\n");

        var work = await new CliFileDiffReader(new()).ReadAsync(fixture.Root, Assert.Single((await fixture.ReadAsync()).Files), false, false, fixture.Token);

        Assert.Equal(7, work.Lines.Count(line => line.Kind is ' ' or '+'));
        Assert.Equal(7, work.Lines.Single(line => line.Text == "+LAST").NewNumber);
        await fixture.CommitAsync("After", "sample.txt");
        var history = new CliHistoryReader(new());
        var details = await history.ReadCommitAsync(fixture.Root, (await fixture.ReadAsync()).Head!, fixture.Token);
        var committed = await history.ReadDiffAsync(fixture.Root, details, Assert.Single(details.Files), false, fixture.Token);
        Assert.Equal(7, committed.Lines.Count(line => line.Kind is ' ' or '+'));
    }
}
