using Trag.Domain.Naming;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Naming;

// The lines of the repository's .gitignore that would ignore an untracked file: the file itself, every file with its
// extension, or its folder, anchored at the root and with Git's special characters escaped.
public sealed class IgnoreChoicesTests
{
    private static (string, string)? Line(IgnoreChoice? choice) => choice is { } some ? (some.Pattern, some.Label) : null;

    [Fact]
    public void WhenAnUntrackedFileIsIgnored_ShouldOfferItItsExtensionAndItsFolderFromTheRoot()
    {
        var path = Snapshots.Path("src/web/app.log");

        Assert.Equal(("/src/web/app.log", IgnoreText.ThisFile), Line(IgnoreChoices.File(path)));
        Assert.Equal(("*.log", IgnoreText.All("*.log")), Line(IgnoreChoices.Extension(path)));
        Assert.Equal(("/src/web/", IgnoreText.Folder("src/web/")), Line(IgnoreChoices.Folder(path)));
        Assert.Same(path, IgnoreChoices.File(path)!.Path);
    }

    // Git lists an untracked folder whole only when it is another repository: the folder is the one line to offer.
    [Fact]
    public void WhenTheUntrackedEntryIsAFolder_ShouldOfferTheFolderAlone()
    {
        var path = Snapshots.Path("inner/");

        Assert.Equal((null, null), (Line(IgnoreChoices.File(path)), Line(IgnoreChoices.Extension(path))));
        Assert.Equal(("/inner/", IgnoreText.Folder("inner/")), Line(IgnoreChoices.Folder(path)));
    }

    [Fact]
    public void WhenTheFileHasNoExtensionOrSitsAtTheRoot_ShouldOfferNoneOfThose()
    {
        Assert.Null(IgnoreChoices.Extension(Snapshots.Path("tools/Makefile")));
        Assert.Null(IgnoreChoices.Folder(Snapshots.Path("notes.txt")));
    }

    // Ignore is offered for an untracked file whose name gives at least one line; never for a tracked file, and not for a
    // name that is not text of its own, which gives none.
    [Fact]
    public void WhenTheFileIsTrackedOrItsNameGivesNoLine_ShouldOfferNothingToIgnore()
    {
        Assert.True(IgnoreChoices.Offered(Snapshots.Untracked("Makefile")));
        Assert.True(IgnoreChoices.Offered(Snapshots.Untracked("logs/a\nb")));
        Assert.False(IgnoreChoices.Offered(new FileStatus(new GitPath([(byte)'a', 0xFF]), '?', '?')));
        Assert.False(IgnoreChoices.Offered(Snapshots.Modified("Makefile")));
    }

    // A star, a question mark, an opening bracket and a backslash are Git's pattern characters, and trailing spaces would
    // be dropped from the line.
    [Fact]
    public void WhenTheNameHoldsGitsPatternCharacters_ShouldEscapeThem()
    {
        Assert.Equal("/logs/a\\*b\\[1]\\?.txt", IgnoreChoices.File(Snapshots.Path("logs/a*b[1]?.txt"))!.Pattern);
        Assert.Equal("/notes.txt\\ \\ ", IgnoreChoices.File(Snapshots.Path("notes.txt  "))!.Pattern);
        Assert.Equal("/back\\\\slash/", IgnoreChoices.Folder(Snapshots.Path("back\\slash/a.txt"))!.Pattern);
    }

    // A name that is not its own UTF-8 would become another name in the file, and Git could not be asked about it.
    [Fact]
    public void WhenTheNameIsNotText_ShouldOfferNothing()
    {
        var latin = new Trag.Domain.Paths.GitPath([0x64, 0x2F, 0x63, 0xE8, 0x2E, 0x74, 0x78, 0x74]);
        Assert.Null(IgnoreChoices.File(latin));
        Assert.Null(IgnoreChoices.Extension(latin));
        Assert.Null(IgnoreChoices.Folder(latin));
    }

    // A line break cannot be written into a line of the file.
    [Fact]
    public void WhenTheNameHoldsALineBreak_ShouldOfferNothingThatHoldsIt()
    {
        Assert.Null(IgnoreChoices.File(Snapshots.Path("a\nb.txt")));
        Assert.Null(IgnoreChoices.Folder(Snapshots.Path("a\rb/c.txt")));
        Assert.Equal("*.txt", IgnoreChoices.Extension(Snapshots.Path("a\nb.txt"))!.Pattern);
    }
}
