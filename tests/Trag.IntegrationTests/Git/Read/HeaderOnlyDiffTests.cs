using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// Changes Git describes with headers alone, no line of content: a file made executable, an empty file added (a .gitkeep,
// an __init__.py) or deleted. The status lists each of them, so the diff pane must say what changed instead of staying blank.
public sealed class HeaderOnlyDiffTests
{
    [Fact]
    public async Task WhenOnlyTheModeOfAStagedFileChanged_ShouldSayWhyNoLineIsShown()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("run.sh", "echo hi\n");
        await fixture.CommitAsync("Before", "run.sh");
        await fixture.GitAsync("update-index", "--chmod=+x", "run.sh");

        var staged = Assert.Single((await fixture.ReadAsync()).Files);
        var diff = await new CliFileDiffReader(new()).ReadAsync(fixture.Root, staged, true, false, fixture.Token);

        Assert.DoesNotContain(diff.Lines, line => !line.IsHeader);
        Assert.Equal(FileDiffText.ModeChanged("100644", "100755"), diff.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenAnEmptyFileIsStagedAsAddedOrDeleted_ShouldSayWhyNoLineIsShown(bool deleted)
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("keep.txt", "kept\n");
        await fixture.CommitAsync("Before", "keep.txt");
        await fixture.WriteAsync(".gitkeep", "");
        if (deleted)
        {
            await fixture.CommitAsync("Add an empty file", ".gitkeep");
            await fixture.GitAsync("rm", "-q", ".gitkeep");
        }
        else await fixture.GitAsync("add", ".gitkeep");

        var staged = Assert.Single((await fixture.ReadAsync()).Files);
        var diff = await new CliFileDiffReader(new()).ReadAsync(fixture.Root, staged, true, false, fixture.Token);

        Assert.DoesNotContain(diff.Lines, line => !line.IsHeader);
        Assert.Equal(deleted ? FileDiffText.EmptyFileDeleted : FileDiffText.NewEmptyFile, diff.Message);
    }
}
