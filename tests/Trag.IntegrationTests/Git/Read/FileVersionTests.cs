using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// A commit's version of a file saved where the user chose, by real Git: the content a checkout would give, line endings
// and all, and a file already there left whole when Git cannot give the version.
public sealed class FileVersionTests
{
    private static async Task<(GitFixture Fixture, string First, string Folder)> TwoVersionsAsync()
    {
        var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("src/a b.txt", "one\n");
        var first = await f.CommitAsync("one", "src/a b.txt");
        await f.WriteAsync("src/a b.txt", "two\n");
        await f.CommitAsync("two", "src/a b.txt");
        var folder = Directory.CreateDirectory(Path.Combine(f.Container, "saved")).FullName;
        return (f, first, folder);
    }

    private static Task SaveAsync(GitFixture f, string commit, string path, string destination) =>
        new CliHistoryReader(f.Runner).SaveFileAsync(f.Root, commit, Snapshots.Path(path), destination, f.Token);

    [Fact]
    public async Task WhenACommitsVersionIsSaved_ShouldWriteItsContentWhereAsked()
    {
        var (f, first, folder) = await TwoVersionsAsync();
        using var _ = f;
        var destination = Path.Combine(folder, "a b.txt");

        await SaveAsync(f, first, "src/a b.txt", destination);

        Assert.Equal("one\n", await File.ReadAllTextAsync(destination, f.Token));
        Assert.Equal("two\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "src", "a b.txt"), f.Token));
    }

    // Git's checkout filters apply: with autocrlf the saved file has the line endings a checkout would give it.
    [Fact]
    public async Task WhenTheRepositoryConvertsLineEndings_ShouldSaveTheVersionAsACheckoutWould()
    {
        var (f, first, folder) = await TwoVersionsAsync();
        using var _ = f;
        await f.GitAsync("config", "core.autocrlf", "true");
        var destination = Path.Combine(folder, "a b.txt");

        await SaveAsync(f, first, "src/a b.txt", destination);

        Assert.Equal("one\r\n", await File.ReadAllTextAsync(destination, f.Token));
    }

    [Fact]
    public async Task WhenGitCannotGiveTheVersion_ShouldFailAndLeaveTheChosenFileWhole()
    {
        var (f, first, folder) = await TwoVersionsAsync();
        using var _ = f;
        var destination = Path.Combine(folder, "kept.txt");
        await File.WriteAllTextAsync(destination, "kept\n", f.Token);

        await Assert.ThrowsAnyAsync<Exception>(() => SaveAsync(f, first, "missing.txt", destination));

        Assert.Equal("kept\n", await File.ReadAllTextAsync(destination, f.Token));
        Assert.Equal([destination], Directory.GetFiles(folder));
    }
}
