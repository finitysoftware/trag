using System.Text;
using Trag.Domain.Diff;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Read;

namespace Trag.Infrastructure.UnitTests.Git.Parsing;

public sealed class FileDiffParserTests
{
    [Fact]
    public void WhenAPatchHasCarriageReturnsAndNoFinalNewline_ShouldAttachEachToTheCorrectSide()
    {
        var diff = FileDiffParser.Patch([], Encoding.UTF8.GetBytes("@@ -1,2 +1,2 @@\n same\r\n-old\r\n\\ No newline at end of file\n+new\n\\ No newline at end of file\n"), false);
        Assert.True(diff.Lines[1].HasCarriageReturn);
        Assert.Equal(" same", diff.Lines[1].Text);
        Assert.Equal("-old\r", diff.Lines[2].Text);
        Assert.False(diff.Lines[2].HasCarriageReturn); Assert.True(diff.Lines[2].NoNewline);
        Assert.True(diff.Lines[3].NoNewline); Assert.Equal(2, diff.Lines[3].NewNumber);
    }

    // diff.suppressBlankEmpty, a setting of the user's that Trag does not override, makes Git print an unchanged blank line
    // as an empty line instead of a single space. It is still a line of the file on both sides.
    [Fact]
    public void WhenABlankContextLineArrivesWithoutItsSpace_ShouldKeepItAsAnUnchangedLineAndCountIt()
    {
        var diff = FileDiffParser.Patch([], "@@ -1,4 +1,4 @@\n a\n\n b\n-c\n+C\n"u8.ToArray(), false);

        Assert.Equal((' ', (int?)2, (int?)2), (diff.Lines[2].Kind, diff.Lines[2].OldNumber, diff.Lines[2].NewNumber));
        Assert.Equal((4, 4), (diff.Lines[4].OldNumber, diff.Lines[5].NewNumber));
    }

    // A patch of headers alone: a mode that changed (chmod +x), an empty file added or an empty file deleted. The view
    // shows no header row, so without a message the pane is blank under a "Modified" or "New File" badge.
    [Theory]
    [InlineData("diff --git a/run.sh b/run.sh\nold mode 100644\nnew mode 100755\n", "Only the file mode changed · 100644 → 100755.")]
    [InlineData("diff --git a/.gitkeep b/.gitkeep\nnew file mode 100644\nindex 0000000..e69de29\n", "New empty file.")]
    [InlineData("diff --git a/.gitkeep b/.gitkeep\ndeleted file mode 100644\nindex e69de29..0000000\n", "Empty file deleted.")]
    [InlineData("diff --git a/a.txt b/a.txt\nindex e69de29..e69de29 100644\n", "No content changes in this source.")]
    public void WhenAPatchCarriesOnlyHeaders_ShouldSayWhyThereIsNoLineToShow(string patch, string message)
    {
        var diff = FileDiffParser.Patch([], Encoding.UTF8.GetBytes(patch), false);

        Assert.Equal(message, diff.Message);
    }

    // A rename read without rename detection is the deletion of the old path and the addition of the new one; for an
    // empty file both sections are headers alone, and the file under its Renamed badge is neither new nor deleted.
    [Fact]
    public void WhenARenamedEmptyFileArrivesAsItsDeletionAndItsAddition_ShouldSayItsContentDidNotChange()
    {
        var diff = FileDiffParser.Patch([], "diff --git a/e.txt b/e.txt\ndeleted file mode 100644\nindex e69de29..0000000\ndiff --git a/f.txt b/f.txt\nnew file mode 100644\nindex 0000000..e69de29\n"u8.ToArray(), false);

        Assert.Equal(FileDiffText.NoChanges, diff.Message);
    }

    // A mode change beside a content change shows the content; the message stays empty, as for every diff with lines.
    [Fact]
    public void WhenTheModeChangedTogetherWithTheContent_ShouldShowTheLinesWithoutAMessage()
    {
        var diff = FileDiffParser.Patch([], "diff --git a/run.sh b/run.sh\nold mode 100644\nnew mode 100755\n@@ -1 +1 @@\n-a\n+b\n"u8.ToArray(), false);

        Assert.Equal("", diff.Message);
        Assert.Equal(2, diff.Lines.Count(line => line.IsChange));
    }

    // Only a CR right before LF ends a line; a bare CR is content.
    [Fact]
    public void WhenANewFileHasCarriageReturnsAndNoFinalNewline_ShouldAttachEachToTheCorrectLine()
    {
        var added = FileDiffParser.NewContent([], Encoding.UTF8.GetBytes("one\r\ntwo\rthree"), false, false);
        Assert.True(added.Lines[0].HasCarriageReturn); Assert.False(added.Lines[0].NoNewline);
        Assert.Equal("+two\rthree", added.Lines[1].Text); Assert.True(added.Lines[1].NoNewline);
    }

    [Fact]
    public void WhenANewFileIsEmpty_ShouldShowNoLines() => Assert.Empty(FileDiffParser.NewContent([], [], false, false).Lines);

    // An untracked file's content: every row is an addition, numbered from 1 on the new side.
    [Fact]
    public void WhenNewContentIsShown_ShouldNumberItsRowsFromOneOnTheNewSide() =>
        Assert.Equal([('+', (int?)null, (int?)1), ('+', (int?)null, (int?)2)],
            FileDiffParser.NewContent([], "one\ntwo\n"u8.ToArray(), false, false).Lines.Select(line => (line.Kind, line.OldNumber, line.NewNumber)));

    [Theory]
    [InlineData("", FileDiffText.NewEmptyFile)]
    [InlineData("one\n", FileDiffText.NewFile)]
    public void WhenNewContentIsShown_ShouldSayWhetherTheFileHasAnyContent(string content, string message) =>
        Assert.Equal(message, FileDiffParser.NewContent([], Encoding.UTF8.GetBytes(content), false, false).Message);

    // Content with a NUL byte is binary, as Git takes it: no rows, a message instead.
    [Fact]
    public void WhenNewContentHoldsANulByte_ShouldShowItAsBinary()
    {
        var content = FileDiffParser.NewContent([], "PK\0data"u8.ToArray(), false, false);
        Assert.Equal((DiffContentKind.Binary, FileDiffText.BinaryPreview, 0), (content.Kind, content.Message, content.Lines.Count));
    }

    // A file that starts with an empty line, shown as Git 2.55's diff of it shows the rows: an empty one, then the rest.
    [Fact]
    public void WhenContentStartsWithAnEmptyLine_ShouldShowItAsAnEmptyRow() =>
        Assert.Equal(["+", "+second"], FileDiffParser.NewContent([], "\nsecond\n"u8.ToArray(), false, false).Lines.Select(line => line.Text));

    [Fact]
    public void WhenUnchangedOrDeletedContentIsPreviewed_ShouldShowItAsOldContextNeverAsAdded()
    {
        var preview = FileDiffParser.NewContent([], "one\ntwo"u8.ToArray(), true, true, contentOnly: true, oldContent: true);
        Assert.All(preview.Lines, line => { Assert.Equal(' ', line.Kind); Assert.Null(line.NewNumber); Assert.NotNull(line.OldNumber); Assert.False(line.NoNewline); });
    }

    [Fact]
    public async Task WhenThePathIsNotValidUtf8_ShouldShowNoContentAndNeverSwapItForAnotherPath()
    {
        var reader = new CliFileDiffReader(new());
        var file = new FileStatus(new([255]), '?', '?');
        var result = await reader.ReadAsync("nonexistent", file, false, false, CancellationToken.None);
        Assert.Equal(DiffContentKind.Unavailable, result.Kind);
        Assert.Empty(result.Lines);
        Assert.Equal(255, file.Path.Bytes[0]);
    }

    // The path comes from the -z raw records, not from patch headers, so a tab, a newline and a byte outside UTF-8 survive.
    [Fact]
    public void WhenRawMetadataIsRead_ShouldKeepThePathBytesAndBothObjects()
    {
        var bytes = Encoding.UTF8.GetBytes(":100644 100644 abc def M\0tab\tline\n").Concat(new byte[] { 255, 0 }).ToArray();
        var file = Assert.Single(FileDiffParser.Metadata(bytes));
        Assert.Equal(255, file.Path.Bytes[^1]);
        Assert.Equal("abc", file.OldObject);
        Assert.Equal("def", file.NewObject);
    }

    // --raw -z as Git 2.55 prints a mode change (git update-index --chmod=+x): each mode as Git printed it, the old one
    // without the record's colon. The modes tell a submodule and a deleted file.
    [Fact]
    public void WhenRawMetadataIsRead_ShouldKeepBothModesWithoutTheColon()
    {
        const string blob = "fa11a6a9c54797a8f68963af8ffc4d92bbffc660";
        var file = Assert.Single(FileDiffParser.Metadata(Encoding.UTF8.GetBytes(":100644 100755 " + blob + " " + blob + " M\0run.sh\0")));
        Assert.Equal(("100644", "100755", "M"), (file.OldMode, file.NewMode, file.Status));
    }

    [Fact]
    public void WhenARawRecordHasNoPath_ShouldRejectIt() => Assert.Throws<FormatException>(() => FileDiffParser.Metadata(":100644\0"u8.ToArray()));

    [Fact]
    public void WhenChangedLinesLookLikeFileHeaders_ShouldNumberThemAsChangesInTheHunk()
    {
        var diff = FileDiffParser.Patch([], Encoding.UTF8.GetBytes("--- a/file\n+++ b/file\n@@ -5,2 +8,2 @@\n common\n--- removed\n+++ added\n"), false);
        Assert.Equal((5, 8), (diff.Lines[3].OldNumber, diff.Lines[3].NewNumber));
        Assert.Equal('-', diff.Lines[4].Kind);
        Assert.Equal(6, diff.Lines[4].OldNumber);
        Assert.Equal('+', diff.Lines[5].Kind);
        Assert.Equal(9, diff.Lines[5].NewNumber);
    }

    [Fact]
    public void WhenALineIsHuge_ShouldShowNoLinesButOfferAPreview()
    {
        var bytes = HugeLine();
        var summary = FileDiffParser.Patch([], bytes, false);
        Assert.Equal(DiffContentKind.Limited, summary.Kind);
        Assert.Empty(summary.Lines);
        Assert.True(summary.CanPreview);
    }

    [Fact]
    public void WhenADiffWithAHugeLineIsPreviewed_ShouldShowABoundedPart()
    {
        var bytes = HugeLine();
        var preview = FileDiffParser.Patch([], bytes, true);
        Assert.InRange(preview.Lines.Count, 1, 200);
        Assert.All(preview.Lines, line => Assert.True(line.Text.Length <= 8193));
    }

    [Fact]
    public void WhenThereAreTooManyLines_ShouldShowNoLinesButOfferAPreview()
    {
        var bytes = ManyLines();
        var summary = FileDiffParser.Patch([], bytes, false);
        Assert.Equal(DiffContentKind.Limited, summary.Kind);
        Assert.Empty(summary.Lines);
        Assert.True(summary.CanPreview);
    }

    [Fact]
    public void WhenADiffWithTooManyLinesIsPreviewed_ShouldShowABoundedPart()
    {
        var bytes = ManyLines();
        var preview = FileDiffParser.Patch([], bytes, true);
        Assert.InRange(preview.Lines.Count, 1, 200);
        Assert.All(preview.Lines, line => Assert.True(line.Text.Length <= 8193));
    }

    // A rename read without rename detection is a deletion and an addition, and Git's output is cut at the byte limit, which
    // can fall inside the hunk header of the addition.
    [Theory]
    [InlineData("@@ ")]
    [InlineData("@@ -")]
    [InlineData("@@ -0,0")]
    [InlineData("@@ -0,0 ")]
    [InlineData("@@ -0,0 +")]
    public void WhenTheCutOutputEndsInsideAHunkHeader_ShouldGiveTheLimitedDiff(string cut)
    {
        var bytes = Encoding.UTF8.GetBytes("diff --git a/old.txt b/old.txt\ndeleted file mode 100644\n--- a/old.txt\n+++ /dev/null\n@@ -1,2 +0,0 @@\n-one\n-two\n"
            + "diff --git a/new.txt b/new.txt\nnew file mode 100644\n--- /dev/null\n+++ b/new.txt\n" + cut);

        var summary = FileDiffParser.Patch([], bytes, false, outputTruncated: true);

        Assert.Equal((DiffContentKind.Limited, true), (summary.Kind, summary.CanPreview));
    }

    // Lines under a hunk header that cannot be read are not numbered on from the hunk before it.
    [Fact]
    public void WhenAHunkHeaderCannotBeRead_ShouldNumberNoLineUnderIt()
    {
        var diff = FileDiffParser.Patch([], "@@ -1,2 +1,2 @@\n a\n-b\n+c\n@@ -x +y @@\n d\n"u8.ToArray(), false);

        Assert.Equal(('h', (int?)null, (int?)null), (diff.Lines[^1].Kind, diff.Lines[^1].OldNumber, diff.Lines[^1].NewNumber));
    }

    // A row of exactly 8,192 characters is shown whole, and the diff is not limited for it.
    [Fact]
    public void WhenALineIsExactlyAtTheLengthLimit_ShouldShowItWhole()
    {
        var diff = FileDiffParser.Patch([], Encoding.UTF8.GetBytes("@@ -0,0 +1 @@\n+" + new string('a', 8191) + "\n"), false);
        Assert.Equal((DiffContentKind.Text, 8192), (diff.Kind, diff.Lines[1].Text.Length));
    }

    // The headers Git prints before the rows are not rows, and its "\ No newline at end of file" after the last row
    // describes that row: a diff of exactly the row limit is shown whole either way.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WhenADiffHasExactlyTheRowLimit_ShouldShowEveryRow(bool newline)
    {
        var patch = "diff --git a/a.txt b/a.txt\nnew file mode 100644\nindex 0000000..d00491f\n--- /dev/null\n+++ b/a.txt\n@@ -0,0 +1,50000 @@\n"
            + string.Concat(Enumerable.Repeat("+x\n", DiffLimits.LineLimit)) + (newline ? "" : "\\ No newline at end of file\n");

        var diff = FileDiffParser.Patch([], Encoding.UTF8.GetBytes(patch), false);

        Assert.Equal((DiffContentKind.Text, DiffLimits.LineLimit, !newline), (diff.Kind, diff.Lines.Count(line => !line.IsHeader), diff.Lines[^1].NoNewline));
    }

    // A file of 24,000 lines that all changed: the 48,000 rows under the limit are counted as they come, so the parse
    // grows with the rows. Measured on 03.11.2026 under the load of parallel suites: tens of milliseconds, against seconds
    // when each row counts all the rows before it again.
    [Fact]
    public void WhenADiffOfFortyEightThousandRowsIsParsed_ShouldTakeLessThanASecond()
    {
        var patch = Encoding.UTF8.GetBytes("diff --git a/large.txt b/large.txt\nindex 1111111..2222222 100644\n--- a/large.txt\n+++ b/large.txt\n@@ -1,24000 +1,24000 @@\n"
            + string.Concat(Enumerable.Range(0, 24_000).Select(line => $"-old line {line:D5}\n")) + string.Concat(Enumerable.Range(0, 24_000).Select(line => $"+new line {line:D5}\n")));
        var fastest = TimeSpan.MaxValue;
        FileDiff? diff = null;

        for (var run = 0; run < 3; run++)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            diff = FileDiffParser.Patch([], patch, false);
            fastest = timer.Elapsed < fastest ? timer.Elapsed : fastest;
        }

        Assert.Equal((DiffContentKind.Text, 48_000), (diff!.Kind, diff.Lines.Count(line => !line.IsHeader)));
        Assert.True(fastest < TimeSpan.FromSeconds(1), $"The fastest of three parses took {fastest.TotalMilliseconds:F0} ms.");
    }

    // Git's "\ No newline at end of file" describes the row before it; with no row before it, it is a row of its own.
    [Fact]
    public void WhenTheNoNewlineMarkerComesFirst_ShouldKeepItAsARow() =>
        Assert.Equal(["\\ No newline at end of file", "-a"], FileDiffParser.Patch([], "\\ No newline at end of file\n-a\n"u8.ToArray(), false).Lines.Select(line => line.Text));

    // The row limit bounds the reading as well as the rows: a diff or content of a million rows is read no further than
    // the 50,000 it may hold, some 6 MB of allocations where reading on to the end takes 30 MB or more.
    [Fact]
    public void WhenTheRowLimitIsReached_ShouldReadNoFurther()
    {
        var bytes = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("+x\n", 1_000_000)));
        var patch = Encoding.UTF8.GetBytes("@@ -0,0 +1,1000000 @@\n" + string.Concat(Enumerable.Repeat("+x\n", 1_000_000)));

        Assert.All<Action>([() => FileDiffParser.Patch([], patch, false), () => FileDiffParser.NewContent([], bytes, false, false)], read =>
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            read();
            Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 16 * 1024 * 1024);
        });
    }

    // One added line longer than the 8,192 characters a row may have.
    private static byte[] HugeLine() => Encoding.UTF8.GetBytes("@@ -0,0 +1 @@\n+" + new string('a', 9000));

    // One row more than the 50,000 a diff may have.
    private static byte[] ManyLines() => Encoding.UTF8.GetBytes("@@ -0,0 +1,50001 @@\n" + string.Concat(Enumerable.Repeat("+x\n", 50_001)));
}
