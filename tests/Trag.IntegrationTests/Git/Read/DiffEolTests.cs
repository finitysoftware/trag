using Trag.TestSupport;
using Trag.Infrastructure.Git.Read;

namespace Trag.IntegrationTests.Git.Read;

public sealed class DiffEolTests
{
    [Fact]
    public async Task WhenACrlfFileWithoutAFinalNewlineIsChangedStagedCommittedAndDeleted_ShouldKeepCarriageReturnsAndTheMissingNewlineInEveryDiffWithoutWritingTheIndex()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "core.autocrlf", "false");
        // The diffs are compared header and all; the headers' path prefixes are the user's to choose, and no row shows them.
        await fixture.GitAsync("config", "diff.noprefix", "false"); await fixture.GitAsync("config", "diff.mnemonicPrefix", "false");
        var path = Path.Combine(fixture.Root, "sample.cs");
        await File.WriteAllTextAsync(path, "var old = 1;\r\nreturn old;");
        await fixture.GitAsync("add", "--", "sample.cs"); await fixture.GitAsync("commit", "-m", "Before");
        await File.WriteAllTextAsync(path, "var next = 2;\r\nreturn next;\r\n");
        var reader = new CliFileDiffReader(new());
        var indexBefore = await File.ReadAllBytesAsync(Path.Combine(fixture.Root, ".git", "index"));
        var work = await reader.ReadAsync(fixture.Root, Assert.Single((await fixture.ReadAsync()).Files), false, false, fixture.Token);
        Assert.Contains(work.Lines, line => line.Text == "-return old;" && line.NoNewline && !line.HasCarriageReturn);
        Assert.Contains(work.Lines, line => line.Text == "+return next;" && line.HasCarriageReturn && !line.NoNewline);
        Assert.Equal(indexBefore, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, ".git", "index")));
        await fixture.GitAsync("add", "--", "sample.cs");
        var staged = await reader.ReadAsync(fixture.Root, Assert.Single((await fixture.ReadAsync()).Files), true, false, fixture.Token);
        Assert.Equal(work.Lines, staged.Lines);
        await fixture.GitAsync("commit", "-m", "After");
        var snapshot = await fixture.ReadAsync();
        var history = new CliHistoryReader(new());
        var details = await history.ReadCommitAsync(fixture.Root, snapshot.Head!, fixture.Token);
        var committed = await history.ReadDiffAsync(fixture.Root, details, Assert.Single(details.Files), false, fixture.Token);
        Assert.Equal(staged.Lines, committed.Lines);
        File.Delete(path);
        var deleted = await reader.ReadAsync(fixture.Root, Assert.Single((await fixture.ReadAsync()).Files), false, false, fixture.Token);
        Assert.All(deleted.Lines.Where(l => !l.IsHeader), line => { Assert.True(line.IsDeletion); Assert.True(line.HasCarriageReturn); });
    }
}
