using Trag.Domain.Diff;

namespace Trag.Domain.UnitTests.Diff;

public sealed class LfsPointerTests
{
    private static readonly string Old = new('a', 64), New = new('b', 64);

    // As Git prints them: a hunk header as it is, every other line after its mark.
    private static PatchLine[] Lines(params (char Kind, string Text)[] lines) =>
        [.. lines.Select(line => new PatchLine(line.Kind, line.Kind == 'h' ? line.Text : line.Kind + line.Text))];

    // A Git LFS file's diff is a diff of two pointers; each side is read from the lines it has, and a side with none is
    // a file added or deleted.
    [Fact]
    public void WhenTheLinesArePointers_ShouldReadEachSidesPointer()
    {
        var modified = Lines(('h', "@@ -1,3 +1,3 @@"), (' ', LfsPointer.Version), ('-', "oid sha256:" + Old), ('-', "size 8"), ('+', "oid sha256:" + New), ('+', "size 12"));
        var added = Lines(('h', "@@ -0,0 +1,3 @@"), ('+', LfsPointer.Version), ('+', "oid sha256:" + New), ('+', "size 12"));

        Assert.Equal<(LfsPointer?, LfsPointer?)?>((new LfsPointer(Old, 8), new LfsPointer(New, 12)), LfsPointer.Sides(modified));
        Assert.Equal<(LfsPointer?, LfsPointer?)?>((null, new LfsPointer(New, 12)), LfsPointer.Sides(added));
    }

    // A file that went into Git LFS or came out of it is a pointer on one side only, and its diff is Git's own.
    [Fact]
    public void WhenOnlyOneSideIsAPointer_ShouldReadNoSides()
    {
        var into = Lines(('h', "@@ -1 +1,3 @@"), ('-', "content"), ('+', LfsPointer.Version), ('+', "oid sha256:" + New), ('+', "size 12"));
        var outOf = Lines(('h', "@@ -1,3 +1 @@"), ('-', LfsPointer.Version), ('-', "oid sha256:" + Old), ('-', "size 8"), ('+', "content"));

        Assert.Equal((null, null), (LfsPointer.Sides(into), LfsPointer.Sides(outOf)));
    }

    // Git LFS writes a line for each of its extensions that changed the content between the version and the object name,
    // as the pointer of a file cleaned through lfs.extension.<name>.clean.
    [Fact]
    public void WhenThePointerHasExtensionLines_ShouldReadItAsAPointer()
    {
        var modified = Lines(('h', "@@ -1,3 +1,4 @@"), (' ', LfsPointer.Version), ('-', "oid sha256:" + Old), ('-', "size 8"),
            ('+', "ext-0-upper sha256:" + new string('c', 64)), ('+', "oid sha256:" + New), ('+', "size 12"));

        Assert.Equal<(LfsPointer?, LfsPointer?)?>((new LfsPointer(Old, 8), new LfsPointer(New, 12, Extended: true)), LfsPointer.Sides(modified));
    }

    // Anything but the pointer format is a file of its own: another first line, an object name that is not SHA-256 (the
    // name becomes a path into the LFS storage), a size that is not a number, more lines or a line between that is not an
    // extension's, or no lines at all.
    [Theory]
    [InlineData("hello|oid sha256:{0}|size 8")]
    [InlineData("version https://git-lfs.github.com/spec/v1|oid sha256:abc|size 8")]
    [InlineData("version https://git-lfs.github.com/spec/v1|oid sha256:{1}|size 8")]
    [InlineData("version https://git-lfs.github.com/spec/v1|oid sha1:{0}|size 8")]
    [InlineData("version https://git-lfs.github.com/spec/v1|oid sha256:{0}|size -8")]
    [InlineData("version https://git-lfs.github.com/spec/v1|oid sha256:{0}|size 8|more")]
    [InlineData("version https://git-lfs.github.com/spec/v1|more|oid sha256:{0}|size 8")]
    [InlineData("")]
    public void WhenTheLinesAreNotAPointer_ShouldReadNoSides(string text)
    {
        var lines = text.Length == 0 ? [] : string.Format(text, Old, "../" + new string('a', 61)).Split('|');

        Assert.Null(LfsPointer.Sides(Lines([.. lines.Select(line => ('+', line))])));
    }
}
