using System.Text;
using Trag.Domain.Blame;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// A file blamed by real Git, at a commit or as the working tree has it: the commit that last changed each line, who
// made it and when, its subject, the path the file had in it, and the commit before it to blame further back from.
public sealed class BlameTests
{
    private static Task<FileBlame> BlameAsync(GitFixture f, string? revision, string path, BlameOptions? options = null) =>
        new CliHistoryReader(new()).ReadBlameAsync(f.Root, revision, path, options ?? BlameOptions.None, f.Token);

    private static IEnumerable<(int Number, string Text, string Commit)> Lines(FileBlame blame) =>
        blame.Lines.Select(line => (line.Number, line.Text, line.Commit.Id));

    [Fact]
    public async Task WhenACommittedFileIsBlamed_ShouldNameTheCommitThatLastChangedEachLine()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("a č.txt", "one\ntwo\nthree\n"); var added = await f.CommitAsync("Add a");
        await f.WriteAsync("a č.txt", "one\nTWO\nthree\n"); var changed = await f.CommitAsync("Shout two");

        var blame = await BlameAsync(f, changed, "a č.txt");

        Assert.Equal([(1, "one", added), (2, "TWO", changed), (3, "three", added)], Lines(blame));
        var (first, second) = (blame.Lines[0].Commit, blame.Lines[1].Commit);
        Assert.Same(first, blame.Lines[2].Commit);
        Assert.Equal(("Trag Fixture", "Shout two", "a č.txt", added, "a č.txt"), (second.Author, second.Summary, second.Path.Display, second.Previous, second.PreviousPath!.Display));
        Assert.Equal(long.Parse(await f.GitAsync("log", "-1", "--format=%at", changed)), second.Time.ToUnixTimeSeconds());
        Assert.Equal((null, null, false), (first.Previous, first.PreviousPath, first.IsUncommitted));
        Assert.Equal([(1, "one", added), (2, "two", added), (3, "three", added)], Lines(await BlameAsync(f, added, "a č.txt")));
    }

    // A file kept with CRLF line endings is blamed line by line, each line without its carriage return.
    [Fact]
    public async Task WhenTheFileHasCrLfLineEndings_ShouldBlameEachLineWithoutTheCarriageReturn()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("crlf.txt", "one\r\ntwo\r\n"); var added = await f.CommitAsync("Add");

        var blame = await BlameAsync(f, added, "crlf.txt");

        Assert.Equal([(1, "one", added), (2, "two", added)], Lines(blame));
    }

    // A blame.ignoreRevsFile set for every repository names a file only some of them have, and Git refuses to blame at all
    // where it is missing; the file is still blamed.
    [Fact]
    public async Task WhenTheConfiguredIgnoreRevsFileIsMissing_ShouldStillBlameTheFile()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "blame.ignoreRevsFile", ".git-blame-ignore-revs");
        await f.WriteAsync("a.txt", "one\n"); var added = await f.CommitAsync("Add a");

        var blame = await BlameAsync(f, added, "a.txt");

        Assert.Equal([(1, "one", added)], Lines(blame));
    }

    // Where the repository has that file, the commits it lists are looked past, as git blame does.
    [Fact]
    public async Task WhenTheConfiguredIgnoreRevsFileIsHere_ShouldLookPastTheCommitsItLists()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("a.txt", "one\ntwo\n"); var added = await f.CommitAsync("Add a");
        await f.WriteAsync("a.txt", "one\nTWO\n"); var reformatted = await f.CommitAsync("Reformat");
        await f.WriteAsync(".git-blame-ignore-revs", reformatted + "\n");
        await f.GitAsync("config", "blame.ignoreRevsFile", ".git-blame-ignore-revs");

        var blame = await BlameAsync(f, reformatted, "a.txt");

        Assert.Equal([(1, "one", added), (2, "TWO", added)], Lines(blame));
    }

    // A path the revision does not have is no blame: the read fails, which the blame view says, instead of answering with
    // nothing.
    [Fact]
    public async Task WhenThePathDoesNotExistAtTheRevision_ShouldFail()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("first.txt", "1\n"); var first = await f.CommitAsync("First");
        await f.WriteAsync("later.txt", "1\n"); await f.CommitAsync("Later");

        await Assert.ThrowsAnyAsync<Exception>(() => BlameAsync(f, first, "later.txt"));
    }

    // A line written under an older address is blamed on its author as .mailmap names them, as the history shows them.
    [Fact]
    public async Task WhenALineWasWrittenUnderAnOlderAddress_ShouldBlameItsAuthorAsMailmapNamesThem()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync(".mailmap", "Ana Anić <ana@example.invalid> <ana.old@example.invalid>\n");
        await f.CommitAsync("mailmap", ".mailmap");
        await f.WriteAsync("a.txt", "one\n");
        await f.GitAsync("add", "a.txt");
        await f.GitAsync("commit", "-m", "Add a", "--author=Ana <ana.old@example.invalid>");

        var blame = await BlameAsync(f, null, "a.txt");

        Assert.Equal("Ana Anić", Assert.Single(blame.Lines).Commit.Author);
    }

    // Git prints a blamed commit's author and subject in i18n.logOutputEncoding; they are read as they are under another.
    [Fact]
    public async Task WhenLogOutputEncodingIsCp1250_ShouldReadTheAuthorAndSubjectAsTheyAre()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "user.name", "Marko Petrović");
        await f.WriteAsync("a.txt", "one\n"); var added = await f.CommitAsync("Čvor đak", "a.txt");
        await f.GitAsync("config", "i18n.logOutputEncoding", "cp1250");

        var blame = await BlameAsync(f, added, "a.txt");

        Assert.Equal(("Marko Petrović", "Čvor đak"), (blame.Lines[0].Commit.Author, blame.Lines[0].Commit.Summary));
    }

    // With whitespace ignored, a line that was only indented keeps the commit that wrote it.
    [Fact]
    public async Task WhenWhitespaceIsIgnored_ShouldKeepAnIndentedLineOnTheCommitThatWroteIt()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("a.txt", "one\ntwo\n"); var added = await f.CommitAsync("Add");
        await f.WriteAsync("a.txt", "one\n    two\n"); var indented = await f.CommitAsync("Indent");

        Assert.Equal(indented, (await BlameAsync(f, indented, "a.txt")).Lines[1].Commit.Id);
        Assert.Equal(added, (await BlameAsync(f, indented, "a.txt", new(IgnoreWhitespace: true, DetectMoves: false))).Lines[1].Commit.Id);
    }

    // With moved lines detected, a block moved down the file keeps the commit that wrote it.
    [Fact]
    public async Task WhenMovedLinesAreDetected_ShouldKeepAMovedBlockOnTheCommitThatWroteIt()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        const string Block = "the first line of the block that is long enough to be found\nthe second line of the block that is long enough too\n";
        const string Middle = "middle line one\nmiddle line two\nmiddle line three\n";
        await f.WriteAsync("a.txt", Block + Middle); var added = await f.CommitAsync("Add");
        await f.WriteAsync("a.txt", Middle + Block); var moved = await f.CommitAsync("Move");

        Assert.Equal(moved, (await BlameAsync(f, moved, "a.txt")).Lines[3].Commit.Id);
        Assert.Equal(added, (await BlameAsync(f, moved, "a.txt", new(IgnoreWhitespace: false, DetectMoves: true))).Lines[3].Commit.Id);
    }

    // With moved lines detected, a block moved into the file from another file of the same commit keeps the commit that
    // wrote it.
    [Fact]
    public async Task WhenMovedLinesAreDetected_ShouldKeepABlockMovedFromAnotherFileOnTheCommitThatWroteIt()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        const string Block = "the first line of the block that is long enough to be found\nthe second line of the block that is long enough too\n";
        await f.WriteAsync("b.txt", "kept in b\n" + Block);
        await f.WriteAsync("a.txt", "kept in a\n");
        var added = await f.CommitAsync("Add");
        await f.WriteAsync("b.txt", "kept in b\n");
        await f.WriteAsync("a.txt", "kept in a\n" + Block);
        var moved = await f.CommitAsync("Move the block to a");

        Assert.Equal(moved, (await BlameAsync(f, moved, "a.txt")).Lines[1].Commit.Id);
        Assert.Equal(added, (await BlameAsync(f, moved, "a.txt", new(IgnoreWhitespace: false, DetectMoves: true))).Lines[1].Commit.Id);
    }

    // With moved lines detected, one commit can own lines of the file under two paths: its own lines here, and lines it
    // wrote in another file that moved here later. Each line names the path it had in that commit, so Blame before opens
    // the right file.
    [Fact]
    public async Task WhenOneCommitOwnsLinesUnderTwoPaths_ShouldNameEachLinesOwnPath()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        const string Block = "the first line of the block that is long enough to be found\nthe second line of the block that is long enough too\n";
        await f.WriteAsync("b.txt", "kept in b\n" + Block);
        await f.WriteAsync("a.txt", "kept in a\n");
        var added = await f.CommitAsync("Add");
        await f.WriteAsync("b.txt", "kept in b\n");
        await f.WriteAsync("a.txt", "kept in a\n" + Block);
        var moved = await f.CommitAsync("Move the block to a");

        var blame = await BlameAsync(f, moved, "a.txt", new(IgnoreWhitespace: false, DetectMoves: true));

        Assert.Equal([(added, "a.txt"), (added, "b.txt"), (added, "b.txt")], blame.Lines.Select(line => (line.Commit.Id, line.Commit.Path.Display)));
    }

    // Git blames a binary file as if it had lines; a NUL in what it gives is how Git itself tells a binary file.
    [Fact]
    public async Task WhenABinaryFileIsBlamed_ShouldBeKnownAsBinary()
    {
        using var f = await GitFixture.CreateAsync("main");
        await File.WriteAllBytesAsync(Path.Combine(f.Root, "logo.png"), [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x0D, 0x49], f.Token);
        await f.WriteAsync("a.txt", "one\n");
        var added = await f.CommitAsync("Add");

        Assert.Equal((true, false), ((await BlameAsync(f, added, "logo.png")).IsBinary, (await BlameAsync(f, added, "a.txt")).IsBinary));
    }

    // Only a full commit id reaches Git as the revision: a name could be an option or resolve to something else.
    [Fact]
    public async Task WhenTheRevisionIsNotACommitId_ShouldRefuseIt()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("a.txt", "one\n"); await f.CommitAsync("Add");

        await Assert.ThrowsAsync<FormatException>(() => BlameAsync(f, "--output=x", "a.txt"));
    }

    // A line changed and not committed is nobody's commit yet; the lines Git reads with CR LF are shown without the CR.
    [Fact]
    public async Task WhenTheWorkingTreeChangesALine_ShouldBlameItOnNothingCommittedYet()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("a.txt", "one\r\ntwo\r\n"); var added = await f.CommitAsync("Add a");
        await f.WriteAsync("a.txt", "one\r\nchanged\r\n");

        var blame = await BlameAsync(f, null, "a.txt");

        Assert.Equal([(1, "one", added), (2, "changed", new string('0', added.Length))], Lines(blame));
        Assert.True(blame.Lines[1].Commit.IsUncommitted);
    }

    // The name reaches Git as a path, also one that reads as an option, at a commit and in the working tree.
    [Theory]
    [MemberData(nameof(HostileNameRows.Tracked), MemberType = typeof(HostileNameRows))]
    public async Task WhenAFileHasAHostileName_ShouldBlameItAtACommitAndInTheWorkingTree(string label)
    {
        var name = TestNames.Find(label).Path;
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync(name, "one\ntwo\n"); var added = await f.CommitAsync("Add");
        await f.WriteAsync(name, "one\nchanged\n");

        var committed = await BlameAsync(f, added, name);
        var working = await BlameAsync(f, null, name);

        Assert.Equal([(1, "one", added), (2, "two", added)], Lines(committed));
        Assert.Equal(name, committed.Lines[0].Commit.Path.Display);
        Assert.Equal([(1, "one", added), (2, "changed", new string('0', added.Length))], Lines(working));
    }

    // Each commit names the path the file had in it, so a blame further back starts from the right name.
    [Fact]
    public async Task WhenTheFileWasRenamed_ShouldNameThePathItHadInEachCommit()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("old.txt", "one\ntwo\nthree\nfour\n"); var added = await f.CommitAsync("Add");
        await f.GitAsync("mv", "old.txt", "new.txt");
        await f.WriteAsync("new.txt", "one\ntwo\nthree\nFOUR\n"); var changed = await f.CommitAsync("Rename and change four");

        var blame = await BlameAsync(f, changed, "new.txt");

        Assert.Equal((added, "old.txt"), (blame.Lines[0].Commit.Id, blame.Lines[0].Commit.Path.Display));
        Assert.Equal((changed, "new.txt", "old.txt"), (blame.Lines[3].Commit.Id, blame.Lines[3].Commit.Path.Display, blame.Lines[3].Commit.PreviousPath!.Display));
    }

    // Git quotes a path with a quote in it. Windows allows no such name, so the file comes from a commit made without the
    // working tree, as one from another system would.
    [Fact]
    public async Task WhenThePathHasAQuote_ShouldReadItBackAsItIs()
    {
        using var f = await GitFixture.CreateAsync("main");
        const string Path = "quote \"q\".txt";
        var input = "commit refs/heads/main\ncommitter Fixture <fixture@example.invalid> 1700000000 +0000\ndata 3\nAdd\n"
            + "M 100644 inline \"quote \\\"q\\\".txt\"\ndata 4\none\n\ndone\n";
        Assert.Equal(0, (await new GitProcessRunner().RunAsync(f.Root, ["-c", "core.protectNTFS=false", "fast-import", "--quiet"], f.Token,
            standardInput: Encoding.UTF8.GetBytes(input))).ExitCode);

        var blame = await BlameAsync(f, await f.GitAsync("rev-parse", "main"), Path);

        Assert.Equal(Path, Assert.Single(blame.Lines).Commit.Path.Display);
    }
}
