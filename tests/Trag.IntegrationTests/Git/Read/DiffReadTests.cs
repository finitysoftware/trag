using System.Text;
using Trag.Domain.Diff;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// A working file's diff as Git reads it when the file is unusual or changes under the read: line endings alone, a file
// that went away after the status, a name Git quotes, and a file renamed with a change.
public sealed class DiffReadTests
{
    // Without autocrlf a file whose line endings alone changed differs on every line, and each new line shows its CR.
    [Fact]
    public async Task WhenOnlyLineEndingsChanged_ShouldShowEveryLineAsChangedWithCrlfMarks()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "core.autocrlf", "false");
        await fixture.WriteAsync("eol.txt", "one\ntwo\n");
        await fixture.CommitAsync("lf", "eol.txt");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "eol.txt"), "one\r\ntwo\r\n");

        var diff = await new CliFileDiffReader(fixture.Runner).ReadAsync(fixture.Root, Assert.Single((await fixture.ReadAsync()).Files), false, false, fixture.Token);

        var changed = diff.Lines.Where(line => !line.IsHeader && (line.IsAddition || line.IsDeletion)).ToArray();
        Assert.Equal(4, changed.Length);
        Assert.All(changed.Where(line => line.IsAddition), line => Assert.True(line.HasCarriageReturn));
        Assert.All(changed.Where(line => line.IsDeletion), line => Assert.False(line.HasCarriageReturn));
    }

    // An editor that saves the file with a UTF-8 byte order mark changes its first line by those bytes alone; the diff
    // keeps the mark on the new line instead of losing it.
    [Fact]
    public async Task WhenAFileGainsAUtf8ByteOrderMark_ShouldKeepTheMarkOnTheNewFirstLine()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("bom.cs", "using System;\n");
        await fixture.CommitAsync("no bom", "bom.cs");
        await File.WriteAllBytesAsync(Path.Combine(fixture.Root, "bom.cs"), [0xEF, 0xBB, 0xBF, .. "using System;\n"u8]);

        var diff = await new CliFileDiffReader(fixture.Runner).ReadAsync(fixture.Root, Assert.Single((await fixture.ReadAsync()).Files), false, false, fixture.Token);

        Assert.Contains(diff.Lines, line => line.Text == "-using System;");
        Assert.Contains(diff.Lines, line => line.Text == "+﻿using System;");
    }

    // The file went away after the status listed it as modified: the diff is its deletion, not a failure.
    [Fact]
    public async Task WhenTheWorkingFileDisappearsBetweenStatusAndDiff_ShouldShowADeletion()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("gone.txt", "one\n");
        await fixture.CommitAsync("first", "gone.txt");
        var path = Path.Combine(fixture.Root, "gone.txt");
        await File.WriteAllTextAsync(path, "two\n");
        var file = Assert.Single((await fixture.ReadAsync()).Files);
        File.Delete(path);

        var diff = await new CliFileDiffReader(fixture.Runner).ReadAsync(fixture.Root, file, false, false, fixture.Token);

        Assert.Contains(diff.Lines, line => line.IsDeletion && line.Text == "-one");
        Assert.DoesNotContain(diff.Lines, line => line.IsAddition);
    }

    // A repository inside the working tree that is no submodule: Git lists its folder whole, and the diff says what it is
    // instead of trying to read the folder as a file.
    [Fact]
    public async Task WhenTheRowIsARepositoryInsideTheWorkingTree_ShouldSaySoInsteadOfFailing()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("a.txt", "one\n");
        await fixture.CommitAsync("first");
        var inner = Path.Combine(fixture.Root, "inner");
        await fixture.GitAtAsync(fixture.Root, "init", "-q", inner);
        await File.WriteAllTextAsync(Path.Combine(inner, "b.txt"), "two\n");
        var file = Assert.Single((await fixture.ReadAsync()).Files);

        var diff = await new CliFileDiffReader(fixture.Runner).ReadAsync(fixture.Root, file, false, false, fixture.Token);

        Assert.Equal(("inner/", DiffContentKind.Unavailable, FileDiffText.InnerRepository), (file.Path.Display, diff.Kind, diff.Message));
    }

    // Each side's diff of the file, and the diff of the commit that added it, is the file's own and names it; x.txt,
    // changed beside it, is what "[x].txt" would match as a glob.
    [Theory]
    [MemberData(nameof(HostileNameRows.Tracked), MemberType = typeof(HostileNameRows))]
    public async Task WhenAFileHasAHostileName_ShouldShowItsDiffUntrackedStagedUnstagedAndInItsCommitUnderThatName(string label)
    {
        var name = TestNames.Find(label).Path;
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "core.autocrlf", "false");
        await fixture.WriteAsync("x.txt", "decoy\n");
        await fixture.CommitAsync("Decoy", "x.txt");
        await fixture.WriteAsync("x.txt", "decoy changed\n");
        var reader = new CliFileDiffReader(fixture.Runner);
        async Task<FileDiff> DiffAsync(bool fromIndex) => await reader.ReadAsync(fixture.Root,
            (await fixture.ReadAsync()).Files.Single(file => file.Path.Display == name && (fromIndex ? file.IsStaged : file.IsUnstaged)), fromIndex, false, fixture.Token);

        await fixture.WriteAsync(name, "one\n");
        var untracked = await DiffAsync(fromIndex: false);
        await fixture.GitAsync("add", "--", name);
        var staged = await DiffAsync(fromIndex: true);
        var commit = await fixture.CommitAsync("Add", name);
        await fixture.WriteAsync(name, "two\n");
        var unstaged = await DiffAsync(fromIndex: false);
        var history = new CliHistoryReader(fixture.Runner);
        var details = await history.ReadCommitAsync(fixture.Root, commit, fixture.Token);
        var committed = await history.ReadDiffAsync(fixture.Root, details, Assert.Single(details.Files), false, fixture.Token);

        foreach (var (diff, changes) in new[] { (untracked, new[] { "+one" }), (staged, ["+one"]), (unstaged, ["-one", "+two"]), (committed, ["+one"]) })
        {
            Assert.Equal(name, Assert.Single(diff.Metadata).Path.Display);
            Assert.Equal(changes, diff.Lines.Where(line => line.IsChange).Select(line => line.Text));
        }
    }

    // With core.quotePath on, as it is by default, Git quotes a non-ASCII name in the diff header; the diff still reads.
    [Fact]
    public async Task WhenANonAsciiNameIsQuotedInTheDiffHeader_ShouldStillReadTheDiff()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "core.quotePath", "true");
        await fixture.WriteAsync("čšž.txt", "one\n");
        await fixture.CommitAsync("first", "čšž.txt");
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "čšž.txt"), "two\n");

        var diff = await new CliFileDiffReader(fixture.Runner).ReadAsync(fixture.Root, Assert.Single((await fixture.ReadAsync()).Files), false, false, fixture.Token);

        Assert.Contains(diff.Lines, line => line.Text == "-one");
        Assert.Contains(diff.Lines, line => line.Text == "+two");
    }

    // Keys that change how Git prints a diff in a terminal leave the rows Trag shows and the patch of every change as they
    // are without them, staged and in the working tree. The staged change moves lines and leaves trailing spaces, which is
    // what colorMoved and wsErrorHighlight mark; the file sits in a folder, whose path diff.relative leaves whole in a
    // diff run from the repository root, as Trag runs it. The prefixes of Git's own file headers may change, since no
    // row shows them and a patch writes its own.
    [Theory]
    [InlineData("diff.relative", "true")]
    [InlineData("diff.noprefix", "true")]
    [InlineData("diff.mnemonicPrefix", "true")]
    [InlineData("diff.interHunkContext", "10")]
    [InlineData("color.diff", "always")]
    [InlineData("diff.wsErrorHighlight", "all")]
    [InlineData("diff.colorMoved", "zebra")]
    [InlineData("diff.statGraphWidth", "10")]
    public async Task WhenTheUserConfiguresHowGitPrintsADiff_ShouldReadTheSameDiffAsWithoutIt(string key, string value)
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "core.autocrlf", "false");
        var lines = Enumerable.Range(1, 40).Select(number => "line " + number + "\n").ToArray();
        await fixture.WriteAsync("dir/a.txt", string.Concat(lines));
        await fixture.CommitAsync("first", "dir/a.txt");
        await fixture.WriteAsync("dir/a.txt", string.Concat(lines[5..19]) + "line 20   \n" + string.Concat(lines[20..]) + string.Concat(lines[..5]));
        await fixture.GitAsync("add", "dir/a.txt");
        await fixture.WriteAsync("dir/a.txt", string.Concat(lines[5..19]) + "line 20   \n" + string.Concat(lines[20..29]) + "line thirty\n" + string.Concat(lines[30..]) + string.Concat(lines[..5]));
        var file = Assert.Single((await fixture.ReadAsync()).Files);
        var reader = new CliFileDiffReader(fixture.Runner);
        async Task<FileDiff[]> BothAsync() =>
            [await reader.ReadAsync(fixture.Root, file, true, false, fixture.Token), await reader.ReadAsync(fixture.Root, file, false, false, fixture.Token)];
        var plain = await BothAsync();

        await fixture.GitAsync("config", key, value);
        var configured = await BothAsync();

        Assert.All(plain.Zip(configured), pair =>
        {
            Assert.Equal((DiffContentKind.Text, ""), (pair.First.Kind, pair.First.Message));
            Assert.Equal((pair.First.Kind, pair.First.Message), (pair.Second.Kind, pair.Second.Message));
            Assert.Equal(Shown(pair.First), Shown(pair.Second));
            Assert.Equal(Patch(pair.First, file), Patch(pair.Second, file));
        });
    }

    private static PatchLine[] Shown(FileDiff diff) => [.. DiffPresentationBuilder.Build(diff, "dir/a.txt", default).Lines];

    private static string Patch(FileDiff diff, FileStatus file) =>
        HunkPatch.For(diff.Lines, [.. diff.Lines.Select((line, index) => (line, index)).Where(row => row.line.IsChange).Select(row => row.index)], file.Path, PatchAction.Stage);

    // Each sample of the content corpus, untracked, staged, in its commit and then replaced in the working tree, is either
    // shown as it is, every row of each side giving back that side's content, or not shown with a sentence that says why.
    [Theory]
    [MemberData(nameof(ContentSamples))]
    public async Task WhenAFileHoldsAContentSample_ShouldShowItExactlyOrSayWhyNotInEveryDiff(string sample)
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "core.autocrlf", "false");
        var content = TestContents.Bytes(sample);
        var text = Encoding.UTF8.GetString(content);
        await File.WriteAllBytesAsync(Path.Combine(fixture.Root, "sample.txt"), content, fixture.Token);
        var reader = new CliFileDiffReader(fixture.Runner);
        async Task<FileDiff> DiffAsync(bool fromIndex) => await reader.ReadAsync(fixture.Root, Assert.Single((await fixture.ReadAsync()).Files), fromIndex, false, fixture.Token);

        var untracked = await DiffAsync(fromIndex: false);
        await fixture.GitAsync("add", "sample.txt");
        var staged = await DiffAsync(fromIndex: true);
        var commit = await fixture.CommitAsync("Sample", "sample.txt");
        var history = new CliHistoryReader(fixture.Runner);
        var details = await history.ReadCommitAsync(fixture.Root, commit, fixture.Token);
        var committed = await history.ReadDiffAsync(fixture.Root, details, Assert.Single(details.Files), false, fixture.Token);
        await fixture.WriteAsync("sample.txt", "changed\n");
        var unstaged = await DiffAsync(fromIndex: false);

        foreach (var (diff, before, after) in new[] { (untracked, "", text), (staged, "", text), (committed, "", text), (unstaged, text, "changed\n") })
        {
            if (diff.Kind != DiffContentKind.Text) Assert.NotEqual("", diff.Message);
            else Assert.Equal((before, after), (Side(diff, '+'), Side(diff, '-')));
        }
    }

    public static TheoryData<string> ContentSamples() => [.. TestContents.Hostile];

    // The content of one side as the rows give it back: every row but the other side's changes, with the ending Git gave it.
    private static string Side(FileDiff diff, char other) => string.Concat(diff.Lines.Where(line => !line.IsHeader && line.Kind != other)
        .Select(line => line.Text[1..] + (line.HasCarriageReturn ? "\r" : "") + (line.NoNewline ? "" : "\n")));

    // A changed binary file in the working tree is binary to Git's diff, as one from a commit or an untracked one is.
    [Fact]
    public async Task WhenATrackedBinaryFileIsModifiedInTheWorkingTree_ShouldShowBinary()
    {
        using var fixture = await GitFixture.CreateAsync();
        var path = Path.Combine(fixture.Root, "data.bin");
        await File.WriteAllBytesAsync(path, [0, 1, 2, 3]);
        await fixture.CommitAsync("first", "data.bin");
        await File.WriteAllBytesAsync(path, [0, 1, 2, 4]);

        var diff = await new CliFileDiffReader(fixture.Runner).ReadAsync(fixture.Root, Assert.Single((await fixture.ReadAsync()).Files), false, false, fixture.Token);

        Assert.Equal((DiffContentKind.Binary, FileDiffText.BinaryDiff), (diff.Kind, diff.Message));
        Assert.Empty(diff.Lines);
    }

    // The conflict preview shows the working file Git left with markers; the user deleted it, so the pane says what is left
    // to decide instead of failing to read it.
    [Fact]
    public async Task WhenTheConflictPreviewsWorkingFileIsMissing_ShouldSaySo()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await fixture.WriteAsync("a.txt", "base\n");
        await fixture.CommitAsync("base", "a.txt");
        await fixture.GitAsync("switch", "-c", "topic");
        await fixture.WriteAsync("a.txt", "topic\n");
        await fixture.CommitAsync("topic", "a.txt");
        await fixture.GitAsync("switch", "main");
        await fixture.WriteAsync("a.txt", "main\n");
        await fixture.CommitAsync("main", "a.txt");
        Assert.NotEqual(0, (await fixture.RunAsync("merge", "topic")).ExitCode);
        File.Delete(Path.Combine(fixture.Root, "a.txt"));
        var file = Assert.Single((await fixture.ReadAsync()).Files);

        var diff = await new CliFileDiffReader(fixture.Runner).ReadAsync(fixture.Root, file, false, false, fixture.Token);

        Assert.True(file.IsConflict);
        Assert.Equal((DiffContentKind.Unavailable, FileDiffText.ConflictWithoutWorkingFile), (diff.Kind, diff.Message));
    }

    // A staged rename that also changed a line is diffed as git diff --cached shows it: the whole file as context with
    // only that line changed, not the old file deleted and the new one added.
    [Fact]
    public async Task WhenAStagedRenameIsAlsoModified_ShouldShowTheModificationNotTheWholeFile()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("old.txt", NumberedLines());
        await fixture.CommitAsync("Add", "old.txt");
        await fixture.GitAsync("mv", "old.txt", "new.txt");
        await fixture.WriteAsync("new.txt", NumberedLines().Replace("line 15\n", "line FIFTEEN\n", StringComparison.Ordinal));
        await fixture.GitAsync("add", "new.txt");
        var file = Assert.Single((await fixture.ReadAsync()).Files);

        var diff = await new CliFileDiffReader(fixture.Runner).ReadAsync(fixture.Root, file, true, false, fixture.Token);

        Assert.Equal('R', file.Index);
        AssertOnlyTheFifteenthLineChanged(diff);
    }

    // The same file renamed and changed in a commit is diffed as git show shows it.
    [Fact]
    public async Task WhenACommitRenamesAndModifiesAFile_ShouldShowTheModificationNotTheWholeFile()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("old.txt", NumberedLines());
        await fixture.CommitAsync("Add", "old.txt");
        await fixture.GitAsync("mv", "old.txt", "new.txt");
        await fixture.WriteAsync("new.txt", NumberedLines().Replace("line 15\n", "line FIFTEEN\n", StringComparison.Ordinal));
        var commit = await fixture.CommitAsync("Rename", "new.txt");
        var history = new CliHistoryReader(fixture.Runner);
        var details = await history.ReadCommitAsync(fixture.Root, commit, fixture.Token);
        var file = Assert.Single(details.Files);

        var diff = await history.ReadDiffAsync(fixture.Root, details, file, false, fixture.Token);

        Assert.Equal(("new.txt", "old.txt"), (file.Path.Display, file.OriginalPath?.Display));
        AssertOnlyTheFifteenthLineChanged(diff);
    }

    private static string NumberedLines() => string.Concat(Enumerable.Range(1, 30).Select(number => $"line {number}\n"));

    private static void AssertOnlyTheFifteenthLineChanged(FileDiff diff)
    {
        Assert.Equal(["-line 15", "+line FIFTEEN"], diff.Lines.Where(line => line.IsChange).Select(line => line.Text));
        Assert.Equal(31, diff.Lines.Count(line => !line.IsHeader));
    }
}
