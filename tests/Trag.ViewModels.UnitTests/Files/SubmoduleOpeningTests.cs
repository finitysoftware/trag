using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.ViewModels.UnitTests.Files;

// A submodule's row opens it as a repository of its own, and from there the repository it belongs to opens again.
public sealed class SubmoduleOpeningTests
{
    [Fact]
    public async Task WhenASubmoduleIsOpenedFromItsRowAndLeft_ShouldOpenItThenItsSuperproject()
    {
        var h = new ShellHarness();
        var sub = new FileStatus(Snapshots.Path("sub"), '.', 'M', IsSubmodule: true, SubmoduleIsCheckedOut: true);
        var opened = Path.Combine("fixture", "sub");
        h.Reader.Read = (path, _) => Task.FromResult(path.EndsWith(opened, StringComparison.Ordinal)
            ? Snapshots.Create(path) with { Superproject = "fixture" }
            : Snapshots.Create(path, files: [sub, new FileStatus(Snapshots.Path("a.txt"), '.', 'M')]));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.False(model.Files.OpenSubmoduleCommand.CanExecute(model.Repository.Snapshot!.Files[1]));
        Assert.Equal("", model.Header.Superproject);

        await model.Files.OpenSubmoduleCommand.ExecuteAsync(sub);

        Assert.EndsWith(opened, model.Repository.Snapshot!.Root, StringComparison.Ordinal);
        Assert.Equal("Back to fixture", model.Header.Superproject);
        await model.OpenSuperprojectCommand.ExecuteAsync(null);
        Assert.Equal("fixture", model.Repository.Snapshot!.Root);
    }

    // A write holds the repository: the way back to the superproject waits for it, as Open does, instead of doing nothing.
    [Fact]
    public async Task WhenOpenSuperprojectIsAskedDuringAWrite_ShouldBeDisabledNotSilentlyIgnored()
    {
        var h = new ShellHarness();
        h.Reader.Read = (path, _) => Task.FromResult(Snapshots.Create(path) with { Superproject = "super" });
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("sub");
        var asked = 0;
        model.OpenSuperprojectCommand.CanExecuteChanged += (_, _) => asked++;
        Assert.True(model.OpenSuperprojectCommand.CanExecute(null));

        model.Repository.IsWriting = true;

        Assert.False(model.OpenSuperprojectCommand.CanExecute(null)); Assert.NotEqual(0, asked);
    }

    // A submodule never checked out has an empty folder, from which Git would find the superproject and open it again.
    [Fact]
    public async Task WhenASubmoduleIsNotCheckedOut_ShouldNotOpenIt()
    {
        var h = new ShellHarness();
        var sub = new FileStatus(Snapshots.Path("sub"), 'M', '.', IsSubmodule: true);
        h.Reader.Read = (path, _) => Task.FromResult(Snapshots.Create(path, files: [sub]));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");

        Assert.False(model.Files.OpenSubmoduleCommand.CanExecute(sub));
        await model.Files.OpenSubmoduleCommand.ExecuteAsync(sub);
        Assert.Equal("fixture", model.Repository.Snapshot!.Root);
    }

    [Fact]
    public async Task WhenASubmoduleIsAskedToOpenDuringAWrite_ShouldBeDisabledNotSilentlyIgnored()
    {
        var h = new ShellHarness();
        var sub = new FileStatus(Snapshots.Path("sub"), '.', 'M', IsSubmodule: true, SubmoduleIsCheckedOut: true);
        h.Reader.Read = (path, _) => Task.FromResult(Snapshots.Create(path, files: [sub]));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        var asked = 0;
        model.Files.OpenSubmoduleCommand.CanExecuteChanged += (_, _) => asked++;
        Assert.True(model.Files.OpenSubmoduleCommand.CanExecute(sub));

        model.Repository.IsWriting = true;

        Assert.False(model.Files.OpenSubmoduleCommand.CanExecute(sub)); Assert.NotEqual(0, asked);
    }
}
