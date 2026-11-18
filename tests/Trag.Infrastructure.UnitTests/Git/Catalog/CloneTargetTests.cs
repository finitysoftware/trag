using Trag.Infrastructure.Git.Catalog;

namespace Trag.Infrastructure.UnitTests.Git.Catalog;

// Where a clone may go is decided without Git and without creating anything: every refusal names what to change.
public sealed class CloneTargetTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "trag-clone-target-" + Guid.NewGuid().ToString("N"));

    public CloneTargetTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, recursive: true);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("--upload-pack=calc.exe")]
    [InlineData("https://example.invalid/repo.git\nfetch")]
    public void WhenGitMustNotSeeTheAddress_ShouldRefuseItAndCreateNothing(string address)
    {
        var (target, rejection) = CloneTarget.Resolve(new(address, Path.Combine(root, "target")));
        Assert.Null(target);
        Assert.Equal("The repository address is not valid.", rejection);
        Assert.False(Directory.Exists(Path.Combine(root, "target")));
    }

    // A local repository is a folder, and a folder's path may have spaces.
    [Fact]
    public void WhenTheSourceIsALocalFolderWithSpaces_ShouldAcceptIt()
    {
        var source = Directory.CreateDirectory(Path.Combine(root, "working copy č")).FullName;
        var (target, rejection) = CloneTarget.Resolve(new(source, Path.Combine(root, "clone")));
        Assert.Null(rejection); Assert.Equal(source, target!.Address);
    }

    [Fact]
    public void WhenARemoteAddressHasSpaces_ShouldRefuseIt() =>
        Assert.Equal("The repository address is not valid.", CloneTarget.Resolve(new("https://example.invalid/my repo.git", Path.Combine(root, "clone"))).Rejection);

    [Fact]
    public void WhenTheSourceFolderIsNotThere_ShouldRefuseIt() =>
        Assert.Equal("The repository address is not valid.", CloneTarget.Resolve(new(Path.Combine(root, "missing folder"), Path.Combine(root, "clone"))).Rejection);

    // Only a full path, which never starts with '-', names a local repository.
    [Fact]
    public void WhenARelativeFolderIsNamedLikeAnOption_ShouldRefuseItEvenWhenItIsThere()
    {
        var name = "-u trag-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(name);
        try { Assert.Equal("The repository address is not valid.", CloneTarget.Resolve(new(name, Path.Combine(root, "clone"))).Rejection); }
        finally { Directory.Delete(name); }
    }

    [Fact]
    public void WhenNoFolderIsEntered_ShouldAskForOne()
    {
        const string address = "https://example.invalid/repo.git";
        Assert.Equal("Enter a local folder for the clone.", CloneTarget.Resolve(new(address, "  ")).Rejection);
    }

    // A path that is not full would land in whatever folder Trag was started from, which the dialog never shows.
    [Theory]
    [InlineData("clones\\repo")]
    [InlineData("C:clones\\repo")]
    [InlineData("\\clones\\repo")]
    public void WhenTheFolderIsNotAFullPath_ShouldAskForOne(string folder)
    {
        var (target, rejection) = CloneTarget.Resolve(new("https://example.invalid/repo.git", folder));
        Assert.Null(target);
        Assert.Equal("Enter the full path of a folder for the clone.", rejection);
    }

    [Fact]
    public void WhenAFileIsAtTheFoldersPath_ShouldAskForAFolder()
    {
        const string address = "https://example.invalid/repo.git";
        var file = Path.Combine(root, "occupied");
        File.WriteAllText(file, "not a folder\n");
        Assert.Equal("A file already exists at this path. Choose a folder.", CloneTarget.Resolve(new(address, file)).Rejection);
    }

    [Fact]
    public void WhenTheFolderIsUsed_ShouldAskForAnEmptyOrNewOne()
    {
        const string address = "https://example.invalid/repo.git";
        var used = Directory.CreateDirectory(Path.Combine(root, "used")).FullName;
        File.WriteAllText(Path.Combine(used, "keep.txt"), "mine\n");
        Assert.Equal("Choose an empty or new folder for the clone.", CloneTarget.Resolve(new(address, used)).Rejection);
    }

    // A drive root is never empty, so it is refused as a used folder before the question of its parent arises.
    [Fact]
    public void WhenTheFolderIsADriveRoot_ShouldAskForAnEmptyOrNewOne()
    {
        const string address = "https://example.invalid/repo.git";
        Assert.Equal("Choose an empty or new folder for the clone.", CloneTarget.Resolve(new(address, Path.GetPathRoot(root)!)).Rejection);
    }

    [Fact]
    public void WhenTheTargetIsGood_ShouldTrimItAndSplitItIntoTheFolderAndItsParentWithoutCreatingThem()
    {
        var (target, rejection) = ResolveGoodTarget();
        Assert.Null(rejection);
        Assert.Equal("https://example.invalid/repo.git", target!.Address);
        Assert.Equal(Path.Combine(root, "clones", "repo"), target.Folder);
        Assert.Equal(Path.Combine(root, "clones"), target.Parent);
        Assert.False(Directory.Exists(target.Parent));
    }

    // A Windows name cannot end in white space, so the space is dropped there; elsewhere it is part of the folder asked for.
    [Fact]
    public void WhenTheFolderEndsInWhiteSpace_ShouldDropItOnWindowsAndKeepItElsewhere()
    {
        var folder = Path.Combine(root, "clones", "repo");
        var target = CloneTarget.Resolve(new("https://example.invalid/repo.git", folder + " ")).Target!;
        Assert.Equal(OperatingSystem.IsWindows() ? folder : folder + " ", target.Folder);
    }

    [Fact]
    public void WhenTheFolderIsThereButEmpty_ShouldAcceptIt()
    {
        var target = ResolveGoodTarget().Target!;
        Assert.Null(CloneTarget.Resolve(new(target.Address, Directory.CreateDirectory(Path.Combine(root, "empty")).FullName)).Rejection);
    }

    // The address as typed, with spaces around it, and the folder with a space before it.
    private (CloneTarget? Target, string? Rejection) ResolveGoodTarget() =>
        CloneTarget.Resolve(new(" https://example.invalid/repo.git ", " " + Path.Combine(root, "clones", "repo")));
}
