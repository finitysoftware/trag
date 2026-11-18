using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.UnitTests.Git.State;

public sealed class RepositoryPathGuardTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "trag-guard-" + Guid.NewGuid().ToString("N"));
    public RepositoryPathGuardTests() => Directory.CreateDirectory(Path.Combine(root, "nested"));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public void WhenAPathStaysInsideTheRepository_ShouldReturnItInFullForm() =>
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "nested", "file.txt")), RepositoryPathGuard.SafePath(root, "nested/file.txt"));

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("nested/../../escape.txt")]
    public void WhenAPathLeavesTheRepository_ShouldRefuseIt(string relative) =>
        Assert.Equal("Path outside repository.", Assert.Throws<IOException>(() => RepositoryPathGuard.SafePath(root, relative)).Message);

    [Fact]
    public void WhenThePathIsTheRepositoryRootItself_ShouldRefuseItAsNotInsideItself() => Assert.Throws<IOException>(() => RepositoryPathGuard.SafePath(root, "."));

    [Fact]
    public void WhenAnOrdinaryFileIsChecked_ShouldFindNoReparsePointOnItOrItsFolders()
    {
        var file = OrdinaryFile();
        Assert.False(RepositoryPathGuard.HasLink(root, file));
    }

    [Fact]
    public void WhenTheFileDoesNotExist_ShouldFindNoReparsePointOnItsFolders()
    {
        var file = OrdinaryFile();
        Assert.False(RepositoryPathGuard.HasLink(root, file));
        Assert.False(RepositoryPathGuard.HasLink(root, Path.Combine(root, "nested", "missing.txt")));
    }

    private string OrdinaryFile()
    {
        var file = Path.Combine(root, "nested", "file.txt");
        File.WriteAllText(file, "content");
        return file;
    }
}
