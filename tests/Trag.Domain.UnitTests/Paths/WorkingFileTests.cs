using Trag.Domain.Paths;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Paths;

// A file of the repository as the machine has it: its full path in the working tree, and whether opening it hands it to
// a program that shows it or would run it.
public sealed class WorkingFileTests
{
    [Fact]
    public void WhenAFileIsLookedFor_ShouldFindItUnderTheRepositorysFolder()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "repo"));

        Assert.Equal(Path.Combine(root, "src", "a b.txt"), WorkingFile.PathIn(root, Snapshots.Path("src/a b.txt")));
    }

    // A program, a script or a shortcut would run, and the repository may be someone else's.
    [Theory]
    [InlineData("notes.md", true)]
    [InlineData("README", true)]
    [InlineData("tools/setup.EXE", false)]
    [InlineData("build.bat", false)]
    [InlineData("build.cmd", false)]
    [InlineData("deploy.ps1", false)]
    [InlineData("settings.reg", false)]
    [InlineData("app.lnk", false)]
    [InlineData("page.hta", false)]
    public void WhenAFileIsOpened_ShouldOpenOnlyWhatDoesNotRun(string path, bool opens) => Assert.Equal(opens, WorkingFile.Opens(Snapshots.Path(path)));

    // Scripts that Git for Windows or Python run when opened, packages that install, help files and add-ins.
    [Theory]
    [InlineData("build.sh")]
    [InlineData("build.bash")]
    [InlineData("tool.py")]
    [InlineData("tool.pyw")]
    [InlineData("tool.pyz")]
    [InlineData("tool.pyzw")]
    [InlineData("tool.pyc")]
    [InlineData("tool.pyo")]
    [InlineData("update.msu")]
    [InlineData("tool.pl")]
    [InlineData("tool.rb")]
    [InlineData("help.chm")]
    [InlineData("app.msix")]
    [InlineData("app.msixbundle")]
    [InlineData("app.appx")]
    [InlineData("app.appxbundle")]
    [InlineData("app.appinstaller")]
    [InlineData("panel.settingcontent-ms")]
    [InlineData("types.ps1xml")]
    [InlineData("console.psc1")]
    [InlineData("module.vb")]
    [InlineData("job.ws")]
    [InlineData("part.wsc")]
    [InlineData("widget.gadget")]
    [InlineData("start.jnlp")]
    [InlineData("fix.diagcab")]
    [InlineData("addin.xll")]
    public void WhenAScriptPackageOrAddInIsOpened_ShouldNotOpenIt(string path) => Assert.False(WorkingFile.Opens(Snapshots.Path(path)));
}
