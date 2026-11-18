using System.Text;
using System.Diagnostics;
using Trag.Domain.Merge;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// The merge view's choices with real Git: a side for a whole file (a deletion where that side deleted it), one conflict
// block at a time, the way back to the conflict, and the two refusals that keep conflict markers out of the merge commit.
public sealed class ConflictResolutionTests
{
    // main and feature both change f.txt and g.txt; feature deletes d.txt that main changed. main merges feature.
    private static async Task<GitFixture> Conflicted()
    {
        var f = await GitFixture.CreateAsync("main");
        try
        {
            await f.GitAsync("config", "core.autocrlf", "false");
            await f.WriteAsync("f.txt", "a\nb\nc\n"); await f.WriteAsync("g.txt", "x\n"); await f.WriteAsync("d.txt", "keep\n"); await f.CommitAsync("Base");
            await f.GitAsync("switch", "-c", "feature");
            await f.WriteAsync("f.txt", "a\nB-feature\nc\n"); await f.WriteAsync("g.txt", "x-feature\n"); await f.GitAsync("rm", "-q", "d.txt"); await f.CommitAsync("Feature");
            await f.GitAsync("switch", "main");
            await f.WriteAsync("f.txt", "a\nB-main\nc\n"); await f.WriteAsync("g.txt", "x-main\n"); await f.WriteAsync("d.txt", "keep-main\n"); await f.CommitAsync("Main");
            Assert.NotEqual(0, (await f.RunAsync("merge", "--no-ff", "--no-edit", "feature")).ExitCode);
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    // git.git keeps its AsciiDoc pages under conflict-marker-size=32, so a title underline is never mistaken for a marker:
    // Git then writes markers of 32 characters. main and feature both change guide.adoc's intro; main merges feature.
    private static async Task<GitFixture> ConflictedWithLongMarkers()
    {
        var f = await GitFixture.CreateAsync("main");
        try
        {
            await f.GitAsync("config", "core.autocrlf", "false");
            await f.WriteAsync(".gitattributes", "*.adoc conflict-marker-size=32\n");
            await f.WriteAsync("guide.adoc", "Guide\n=======\n\nintro\n"); await f.CommitAsync("Base");
            await f.GitAsync("switch", "-c", "feature");
            await f.WriteAsync("guide.adoc", "Guide\n=======\n\nintro by feature\n"); await f.CommitAsync("Feature");
            await f.GitAsync("switch", "main");
            await f.WriteAsync("guide.adoc", "Guide\n=======\n\nintro by main\n"); await f.CommitAsync("Main");
            Assert.NotEqual(0, (await f.RunAsync("merge", "--no-ff", "--no-edit", "feature")).ExitCode);
            Assert.Contains(new string('<', 32) + " HEAD", Read(f, "guide.adoc"));
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    // main renames a.txt to b.txt and d.txt to e.txt; feature renames a.txt to c.txt and deletes d.txt. The old name
    // a.txt is left by both (DD), b.txt is main's alone (AU), c.txt feature's alone (UA), e.txt deleted by feature (UD).
    private static async Task<GitFixture> RenamedApart()
    {
        var f = await GitFixture.CreateAsync("main");
        try
        {
            await f.GitAsync("config", "core.autocrlf", "false");
            await f.WriteAsync("a.txt", "one\ntwo\n"); await f.WriteAsync("d.txt", "x\n"); await f.CommitAsync("Base");
            await f.GitAsync("switch", "-c", "feature");
            await f.GitAsync("mv", "a.txt", "c.txt"); await f.GitAsync("rm", "-q", "d.txt"); await f.GitAsync("commit", "-m", "Feature");
            await f.GitAsync("switch", "main");
            await f.GitAsync("mv", "a.txt", "b.txt"); await f.GitAsync("mv", "d.txt", "e.txt"); await f.GitAsync("commit", "-m", "Main");
            Assert.NotEqual(0, (await f.RunAsync("merge", "--no-ff", "--no-edit", "feature")).ExitCode);
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    // A file committed with CRLF under core.autocrlf: the conflict is written with CRLF, and a block or a side taken keeps it.
    [Fact]
    public async Task WhenTheConflictedFileHasCrLf_ShouldKeepCrLfAfterTakingABlockAndASide()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "true");
        await f.WriteAsync("f.txt", "a\r\nb\r\nc\r\n"); await f.WriteAsync("g.txt", "x\r\n"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "feature");
        await f.WriteAsync("f.txt", "a\r\nB-feature\r\nc\r\n"); await f.WriteAsync("g.txt", "x-feature\r\n"); await f.CommitAsync("Feature");
        await f.GitAsync("switch", "main");
        await f.WriteAsync("f.txt", "a\r\nB-main\r\nc\r\n"); await f.WriteAsync("g.txt", "x-main\r\n"); await f.CommitAsync("Main");
        Assert.NotEqual(0, (await f.RunAsync("merge", "--no-ff", "--no-edit", "feature")).ExitCode);
        var before = await f.ReadAsync();
        var text = await new CliFileDiffReader(f.Runner).ReadConflictAsync(f.Root, before.Conflicts.Single(file => file.Path.Display == "f.txt"), f.Token);

        var block = await Write(f, new ConflictBlockRequest(before, Snapshots.Path("f.txt"), 0, ConflictChoice.Theirs, text.Hash));
        var side = await Write(f, new ConflictSideRequest(await f.ReadAsync(), Snapshots.Path("g.txt"), ConflictChoice.Mine));

        Assert.True(block.Outcome == GitWriteOutcome.Succeeded, block.Message + block.Details);
        Assert.True(side.Outcome == GitWriteOutcome.Succeeded, side.Message + side.Details);
        Assert.Equal("a\r\nB-feature\r\nc\r\n", Read(f, "f.txt"));
        Assert.Equal("x-main\r\n", Read(f, "g.txt"));
    }

    // Under core.fileMode=false, Windows' default, git add gives a conflicted file the executable bit of our stage, so a
    // side has to bring its mode into the index along with its content. feature or main makes s.sh executable.
    [Theory]
    [InlineData("feature", ConflictChoice.Theirs, "100755")]
    [InlineData("feature", ConflictChoice.Mine, "100644")]
    [InlineData("main", ConflictChoice.Theirs, "100644")]
    [InlineData("main", ConflictChoice.Mine, "100755")]
    public async Task WhenTheSidesDifferInTheExecutableBit_ShouldResolveWithTheChosenSidesMode(string executable, ConflictChoice side, string mode)
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false"); await f.GitAsync("config", "core.fileMode", "false");
        await f.WriteAsync("s.sh", "a\n"); await f.CommitAsync("Base");
        foreach (var branch in new[] { "feature", "main" })
        {
            await f.GitAsync("switch", branch == "feature" ? "-c" : "-q", branch);
            await f.WriteAsync("s.sh", "a\n" + branch + "\n"); await f.GitAsync("add", "s.sh");
            if (branch == executable) await f.GitAsync("update-index", "--chmod=+x", "s.sh");
            await f.GitAsync("commit", "-m", branch);
        }
        Assert.NotEqual(0, (await f.RunAsync("merge", "--no-ff", "--no-edit", "feature")).ExitCode);

        var result = await Write(f, new ConflictSideRequest(await f.ReadAsync(), Snapshots.Path("s.sh"), side));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.StartsWith(mode + " ", await f.GitAsync("ls-files", "--stage", "s.sh"));
        Assert.Equal(side == ConflictChoice.Mine ? "a\nmain\n" : "a\nfeature\n", Read(f, "s.sh"));
        Assert.All((await f.ReadAsync()).Files, file => Assert.False(file.IsConflict || file.IsWorkingTreeChange, file.Path.Display));
    }

    // Trag reads status without optional locks, so status never writes refreshed file data back into the index: a side
    // has to leave an entry whose stat data matches the file, or every read hashes it again through its clean filter (LFS
    // on a large binary). The count filter logs each clean it runs. Git also reads a file by content while its time is not
    // older than the index's, to the second; the index is dated a second later, as one written after that second.
    [Fact]
    public async Task WhenASideIsTakenForAFilteredFile_ShouldNotCleanItAgainOnEveryStatusRead()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.GitAsync("config", "filter.count.clean", "sh clean.sh"); await f.GitAsync("config", "filter.count.smudge", "cat");
        await f.WriteAsync("clean.sh", "echo clean >> .git/count.log\ncat\n"); await f.WriteAsync(".gitattributes", "f.bin filter=count\n");
        await f.WriteAsync("f.bin", "a\n"); await f.CommitAsync("Base");
        foreach (var branch in new[] { "feature", "main" })
        {
            await f.GitAsync("switch", branch == "feature" ? "-c" : "-q", branch);
            await f.WriteAsync("f.bin", "a\n" + branch + "\n"); await f.CommitAsync(branch);
        }
        Assert.NotEqual(0, (await f.RunAsync("merge", "--no-ff", "--no-edit", "feature")).ExitCode);
        var used = await Write(f, new ConflictSideRequest(await f.ReadAsync(), Snapshots.Path("f.bin"), ConflictChoice.Theirs));
        var log = Path.Combine(f.Root, ".git", "count.log");
        Assert.True(File.Exists(log));
        File.Delete(log);
        var index = Path.Combine(f.Root, ".git", "index");
        File.SetLastWriteTimeUtc(index, File.GetLastWriteTimeUtc(index).AddSeconds(1));

        for (var read = 0; read < 3; read++) await f.ReadAsync();

        Assert.True(used.Outcome == GitWriteOutcome.Succeeded, used.Message + used.Details);
        Assert.False(File.Exists(log));
    }

    // A binary file has no text to cut into blocks: a side takes the whole file, byte for byte.
    [Fact]
    public async Task WhenTheConflictIsBinary_ShouldShowNoBlocksAndResolveWithASide()
    {
        using var f = await GitFixture.CreateAsync("main");
        var image = Path.Combine(f.Root, "logo.png");
        await File.WriteAllBytesAsync(image, [0x89, 0x50, 0x4E, 0x47, 0, 1], f.Token); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "feature");
        await File.WriteAllBytesAsync(image, [0x89, 0x50, 0x4E, 0x47, 0, 2], f.Token); await f.CommitAsync("Feature");
        await f.GitAsync("switch", "main");
        await File.WriteAllBytesAsync(image, [0x89, 0x50, 0x4E, 0x47, 0, 3], f.Token); await f.CommitAsync("Main");
        Assert.NotEqual(0, (await f.RunAsync("merge", "--no-ff", "--no-edit", "feature")).ExitCode);
        var before = await f.ReadAsync();

        var result = await Write(f, new ConflictSideRequest(before, Snapshots.Path("logo.png"), ConflictChoice.Theirs));

        Assert.Null(before.Resolutions.Open.Single().Blocks);
        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0, 2], await File.ReadAllBytesAsync(image, f.Token));
        Assert.Empty((await f.ReadAsync()).Conflicts);
    }

    // Both sides added the same new file with other content (AA): a text conflict with its block, resolved by a side.
    [Fact]
    public async Task WhenBothSidesAddedTheFile_ShouldShowItsBlockAndResolveItWithASide()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("base.txt", "base\n"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "feature");
        await f.WriteAsync("new.txt", "feature\n"); await f.CommitAsync("Feature adds");
        await f.GitAsync("switch", "main");
        await f.WriteAsync("new.txt", "main\n"); await f.CommitAsync("Main adds");
        Assert.NotEqual(0, (await f.RunAsync("merge", "--no-ff", "--no-edit", "feature")).ExitCode);
        var before = await f.ReadAsync();

        var result = await Write(f, new ConflictSideRequest(before, Snapshots.Path("new.txt"), ConflictChoice.Theirs));

        Assert.Equal(('A', 'A', 1), (before.Conflicts.Single().Index, before.Conflicts.Single().Worktree, before.Resolutions.Open.Single().Blocks));
        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("feature\n", Read(f, "new.txt"));
    }

    // Reopening brings a conflict back from Git's resolve-undo record. Cleared outside Trag after the resolution was
    // shown, there is nothing to bring back: the index is not the one shown, and the resolution stays as it is.
    [Fact]
    public async Task WhenAResolvedFilesResolveUndoWasCleared_ShouldRefuseToReopen()
    {
        using var f = await Conflicted();
        await Write(f, new ConflictSideRequest(await f.ReadAsync(), Snapshots.Path("g.txt"), ConflictChoice.Mine));
        var shown = await f.ReadAsync();
        Assert.Contains(shown.Resolutions.Resolved, path => path.Path.Display == "g.txt" && path.How == Resolution.Mine);
        await f.GitAsync("update-index", "--clear-resolve-undo");

        var result = await Write(f, new ReopenConflictRequest(shown, Snapshots.Path("g.txt")));

        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome);
        Assert.Equal("x-main\n", Read(f, "g.txt"));
        Assert.DoesNotContain((await f.ReadAsync()).Conflicts, file => file.Path.Display == "g.txt");
    }

    // Every path one side has no file at is a question of keeping it; the side without it deletes it, the other keeps
    // its version.
    [Fact]
    public async Task WhenBothSidesRenamedAFileApart_ShouldAskToKeepOrDeleteEachNameAndDeleteItForTheSideWithout()
    {
        using var f = await RenamedApart();
        var conflicts = MergeFiles.From(await f.ReadAsync()).Conflicts;
        Assert.Equal([("a.txt", true), ("b.txt", true), ("c.txt", true), ("e.txt", true)],
            conflicts.Select(conflict => (conflict.File.Path.Display, conflict.IsDeletion)));

        foreach (var (path, side) in new[] { ("a.txt", ConflictChoice.Theirs), ("b.txt", ConflictChoice.Mine), ("c.txt", ConflictChoice.Mine), ("e.txt", ConflictChoice.Mine) })
        {
            var result = await Write(f, new ConflictSideRequest(await f.ReadAsync(), Snapshots.Path(path), side));
            Assert.True(result.Outcome == GitWriteOutcome.Succeeded, path + ": " + result.Message + result.Details);
        }

        Assert.Empty((await f.ReadAsync()).Conflicts);
        Assert.Equal([false, true, false, true], new[] { "a.txt", "b.txt", "c.txt", "e.txt" }.Select(path => File.Exists(Path.Combine(f.Root, path))));
    }

    // Each tracked row of the hostile names with each side.
    public static TheoryData<string, ConflictChoice> HostileSides()
    {
        var rows = new TheoryData<string, ConflictChoice>();
        foreach (string label in HostileNameRows.Tracked)
            foreach (var side in new[] { ConflictChoice.Mine, ConflictChoice.Theirs }) rows.Add(label, side);
        return rows;
    }

    // main and feature both change the second line of the named file and of x.txt, what "[x].txt" would match as a glob.
    // main merges feature.
    private static async Task<GitFixture> ConflictedAs(string name)
    {
        var f = await GitFixture.CreateAsync("main");
        try
        {
            await f.GitAsync("config", "core.autocrlf", "false");
            foreach (var path in new[] { name, "x.txt" }) await f.WriteAsync(path, "a\nb\nc\n");
            await f.CommitAsync("Base");
            await f.GitAsync("switch", "-c", "feature");
            foreach (var path in new[] { name, "x.txt" }) await f.WriteAsync(path, "a\nB-feature\nc\n");
            await f.CommitAsync("Feature");
            await f.GitAsync("switch", "main");
            foreach (var path in new[] { name, "x.txt" }) await f.WriteAsync(path, "a\nB-main\nc\n");
            await f.CommitAsync("Main");
            Assert.NotEqual(0, (await f.RunAsync("merge", "--no-ff", "--no-edit", "feature")).ExitCode);
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    [Theory]
    [MemberData(nameof(HostileSides))]
    public async Task WhenAConflictedFileHasAHostileName_ShouldTakeEitherSideOfTheWholeFile(string label, ConflictChoice side)
    {
        var name = TestNames.Find(label).Path;
        using var f = await ConflictedAs(name);

        var result = await Write(f, new ConflictSideRequest(await f.ReadAsync(), Snapshots.Path(name), side));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        var content = side == ConflictChoice.Mine ? "a\nB-main\nc\n" : "a\nB-feature\nc\n";
        Assert.Equal((content, content.TrimEnd()), (Read(f, name), await f.GitAsync("show", ":" + name, "--")));
        Assert.Equal("x.txt", Assert.Single((await f.ReadAsync()).Conflicts).Path.Display);
    }

    [Theory]
    [MemberData(nameof(HostileNameRows.Tracked), MemberType = typeof(HostileNameRows))]
    public async Task WhenAConflictedFileHasAHostileName_ShouldTakeBothSidesOfABlockAndMarkItResolved(string label)
    {
        var name = TestNames.Find(label).Path;
        using var f = await ConflictedAs(name);
        var before = await f.ReadAsync();
        var text = await new CliFileDiffReader(f.Runner).ReadConflictAsync(f.Root, before.Conflicts.Single(file => file.Path.Display == name), f.Token);

        var both = await Write(f, new ConflictBlockRequest(before, Snapshots.Path(name), 0, ConflictChoice.Both, text.Hash));
        var staged = await Write(f, new StageRequest(await f.ReadAsync(), [Snapshots.Path(name)], Resolution: true));

        Assert.Equal((GitWriteOutcome.Succeeded, GitWriteOutcome.Succeeded), (both.Outcome, staged.Outcome));
        Assert.Equal("a\nB-main\nB-feature\nc\n", Read(f, name));
        Assert.Equal("x.txt", Assert.Single((await f.ReadAsync()).Conflicts).Path.Display);
    }

    // A conflict on caf\xE9.txt, a name that is Latin-1, not UTF-8: Linux keeps such a file as it is, and Windows cannot
    // write it under that name at all, so the merge's three stages go into the index directly. Neither a side nor a tool
    // can be handed that name as text, and the view says to use Git's own tools.
    [Fact]
    public async Task WhenTheConflictPathIsNotUtf8_ShouldGiveAdviceThatWorks()
    {
        using var f = await Conflicted();
        GitPath latin1 = new([(byte)'c', (byte)'a', (byte)'f', 0xE9, (byte)'.', (byte)'t', (byte)'x', (byte)'t']);
        var stages = new List<byte>();
        foreach (var (stage, content) in new[] { (1, "a\n"), (2, "a-main\n"), (3, "a-feature\n") })
        {
            var blob = (await f.Runner.RunAsync(f.Root, ["hash-object", "-w", "--stdin"], f.Token, standardInput: Encoding.UTF8.GetBytes(content))).StandardOutput;
            stages.AddRange(Encoding.ASCII.GetBytes("100644 " + Encoding.ASCII.GetString(blob).Trim() + " " + stage + "\t")); stages.AddRange(latin1.Bytes); stages.Add(0);
        }
        Assert.Equal(0, (await f.Runner.RunAsync(f.Root, ["update-index", "-z", "--index-info"], f.Token, standardInput: [.. stages])).ExitCode);
        var before = await f.ReadAsync();
        var conflict = before.Conflicts.Single(file => file.Path.IsSame(latin1));

        var text = await new CliFileDiffReader(f.Runner).ReadConflictAsync(f.Root, conflict, f.Token);
        var side = await Write(f, new ConflictSideRequest(before, latin1, ConflictChoice.Theirs));
        var tool = await Write(f, new MergeToolRequest(await f.ReadAsync(), latin1, Editor: true));

        Assert.Equal(MergeText.NotTextPath, text.Message);
        Assert.False(new MergeConflict(conflict, null).TakesSides);
        Assert.Equal((GitWriteOutcome.Rejected, ConflictOutcomeText.PathNotNameable), (side.Outcome, side.Message));
        Assert.Equal((GitWriteOutcome.Rejected, ConflictOutcomeText.PathNotNameable), (tool.Outcome, tool.Message));
        Assert.Contains((await f.ReadAsync()).Conflicts, file => file.Path.IsSame(latin1));
    }

    // Git's checkout -m brings a conflict back only by merging both sides' files again: for d.txt, which feature deleted,
    // it says the path "does not have all necessary versions", whichever side resolved it. Reopen is offered only where
    // Git can do it.
    [Theory]
    [InlineData(ConflictChoice.Mine)]
    [InlineData(ConflictChoice.Theirs)]
    public async Task WhenAConflictOneSideDeletedIsResolved_ShouldNotOfferToReopenIt(ConflictChoice side)
    {
        using var f = await Conflicted();
        await Write(f, new ConflictSideRequest(await f.ReadAsync(), Snapshots.Path("d.txt"), side));
        await Write(f, new ConflictSideRequest(await f.ReadAsync(), Snapshots.Path("g.txt"), side));
        var resolved = MergeFiles.From(await f.ReadAsync()).Resolved;

        var reopen = await Write(f, new ReopenConflictRequest(await f.ReadAsync(), Snapshots.Path("d.txt")));

        Assert.Equal([("d.txt", false), ("g.txt", true)], resolved.Select(file => (file.Path.Display, file.CanReopen)).OrderBy(file => file.Display));
        Assert.Equal((GitWriteOutcome.Rejected, ConflictOutcomeText.NotReopened), (reopen.Outcome, reopen.Message));
        Assert.Contains("does not have all necessary versions", reopen.Details);
    }

    // With rerere.enabled and rerere.autoUpdate Git replays a resolution it recorded and stages it too; Trag's merge asks
    // Git not to stage it, so the replayed file stays a conflict with no block left for the user to look at, and reads
    // as the user's own edit once marked resolved.
    [Fact]
    public async Task WhenRerereReplaysAResolution_ShouldLeaveItOpenWithNoBlockAndReadItAsAnEditOnceMarked()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.GitAsync("config", "rerere.enabled", "true"); await f.GitAsync("config", "rerere.autoUpdate", "true");
        await f.WriteAsync("f.txt", "a\nb\nc\n"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "feature"); await f.WriteAsync("f.txt", "a\nB-feature\nc\n"); var feature = await f.CommitAsync("Feature");
        await f.GitAsync("switch", "main"); await f.WriteAsync("f.txt", "a\nB-main\nc\n"); var main = await f.CommitAsync("Main");
        Assert.NotEqual(0, (await f.RunAsync("merge", "--no-ff", "--no-edit", "feature")).ExitCode);
        await f.WriteAsync("f.txt", "a\nB-both\nc\n"); await f.GitAsync("add", "f.txt"); await f.GitAsync("commit", "--no-edit");
        await f.GitAsync("reset", "--hard", main);

        var merged = await Write(f, new MergeRequest(await f.ReadAsync(), "feature", feature, false, "Merge branch 'feature'"));
        var replayed = await f.ReadAsync();
        var staged = await Write(f, new StageRequest(replayed, [Snapshots.Path("f.txt")], Resolution: true));

        Assert.Equal(GitWriteOutcome.NeedsResolution, merged.Outcome);
        Assert.Equal("a\nB-both\nc\n", Read(f, "f.txt"));
        Assert.Equal(0, Assert.Single(replayed.Resolutions.Open).Blocks);
        Assert.True(staged.Outcome == GitWriteOutcome.Succeeded, staged.Message + staged.Details);
        Assert.Equal(Resolution.Edited, Assert.Single((await f.ReadAsync()).Resolutions.Resolved).How);
    }

    // The file a block was read from is gone by the time the block is taken: nothing is written.
    [Fact]
    public async Task WhenTheConflictedFileIsGoneBeforeABlockIsTaken_ShouldSayItIsNotAPlainFile()
    {
        using var f = await Conflicted();
        var before = await f.ReadAsync();
        var text = await new CliFileDiffReader(f.Runner).ReadConflictAsync(f.Root, before.Conflicts.Single(file => file.Path.Display == "f.txt"), f.Token);
        File.Delete(Path.Combine(f.Root, "f.txt"));

        var result = await Write(f, new ConflictBlockRequest(before, Snapshots.Path("f.txt"), 0, ConflictChoice.Mine, text.Hash));

        Assert.Equal((GitWriteOutcome.Rejected, ConflictOutcomeText.NotPlainFile), (result.Outcome, result.Message));
        Assert.False(File.Exists(Path.Combine(f.Root, "f.txt")));
    }

    // The same bytes read with longer markers, after an attribute set them, hold no block: the choice made on the blocks
    // shown is refused and the file stays as it was.
    [Fact]
    public async Task WhenTheMarkerSizeChangesBeforeABlockIsTaken_ShouldSayTheConflictsChanged()
    {
        using var f = await Conflicted();
        var before = await f.ReadAsync();
        var text = await new CliFileDiffReader(f.Runner).ReadConflictAsync(f.Root, before.Conflicts.Single(file => file.Path.Display == "f.txt"), f.Token);
        await File.WriteAllTextAsync(Path.Combine(f.Root, ".git", "info", "attributes"), "*.txt conflict-marker-size=32\n", f.Token);

        var result = await Write(f, new ConflictBlockRequest(before, Snapshots.Path("f.txt"), 0, ConflictChoice.Mine, text.Hash));

        Assert.Equal((GitWriteOutcome.Rejected, ConflictOutcomeText.ConflictsChanged), (result.Outcome, result.Message));
        Assert.Equal(text.Text, Read(f, "f.txt"));
    }

    // A stage list Git cannot give decides nothing: a side missing from it would read as a deletion.
    [Fact]
    public async Task WhenGitCannotListTheSidesOfAConflict_ShouldRefuseAndChangeNothing()
    {
        using var f = await Conflicted();
        var git = await TestProgram.WriteAsync(f.Container, "no-stages-git",
            "@echo %* | findstr /C:\"ls-files --stage\" >nul && exit /b 1\r\n@\"" + new GitProcessRunner().Program + "\" %*\r\n",
            "case \"$*\" in *\"ls-files --stage\"*) exit 1 ;; esac\nexec " + TestProgram.Quote(new GitProcessRunner().Program) + " \"$@\"\n", f.Token);
        var conflicted = Read(f, "g.txt");

        var result = await new GitWriteRouter(new GitProcessRunner(executable: new() { Path = git }))
            .ExecuteAsync(new ConflictSideRequest(await f.ReadAsync(), Snapshots.Path("g.txt"), ConflictChoice.Theirs), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, ConflictOutcomeText.SidesNotListed), (result.Outcome, result.Message));
        Assert.Equal(conflicted, Read(f, "g.txt"));
        Assert.Contains((await f.ReadAsync()).Conflicts, file => file.Path.Display == "g.txt");
    }

    // The folder of a conflicted file was replaced by a link to a folder outside the repository after the merge stopped:
    // Git's status still shows the conflict, and no tool is opened through the link.
    [Fact]
    public async Task WhenTheConflictedFilesFolderIsALink_ShouldNotOpenATool()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("d/f.txt", "a\nb\n"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "feature"); await f.WriteAsync("d/f.txt", "a\nB-feature\n"); await f.CommitAsync("Feature");
        await f.GitAsync("switch", "main"); await f.WriteAsync("d/f.txt", "a\nB-main\n"); await f.CommitAsync("Main");
        Assert.NotEqual(0, (await f.RunAsync("merge", "--no-ff", "--no-edit", "feature")).ExitCode);
        var outside = Path.Combine(f.Container, "outside");
        Directory.Move(Path.Combine(f.Root, "d"), outside);
        await CreateDirectoryLinkAsync(Path.Combine(f.Root, "d"), outside);
        await f.GitAsync("config", "core.editor", "true");

        var result = await Write(f, new MergeToolRequest(await f.ReadAsync(), Snapshots.Path("d/f.txt"), Editor: true));

        Assert.Equal((GitWriteOutcome.Rejected, ConflictOutcomeText.ToolOnLink), (result.Outcome, result.Message));
    }

    private static async Task CreateDirectoryLinkAsync(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await output + await error);
    }

    // f.txt is a real conflict whose working file is replaced by one written the way Git writes a conflicted file (the
    // Domain fuzz tests' generator, fixed seeds, any line endings, a byte order mark, non-ASCII text). Each block is taken
    // through the writer in a random order with a random side, the file read again before each, as the merge view reads
    // it: the file ends as exactly the chosen sides and every byte outside the blocks.
    [Fact]
    public async Task WhenBlocksAreTakenThroughTheWriterInAnyOrderWithAnySide_ShouldWriteTheChosenSidesAndKeepEveryOtherByte()
    {
        using var f = await Conflicted();
        var path = Path.Combine(f.Root, "f.txt");
        var reader = new CliFileDiffReader(f.Runner);
        for (var seed = 0; seed < 6; seed++)
        {
            var random = new Random(seed);
            var file = new ConflictedFileShape(random, new[] { "LF", "CRLF", "Mixed" }[seed % 3]).Conflicted();
            var choices = file.Sides.Select(_ => (ConflictChoice)random.Next(3)).ToArray();
            var order = Enumerable.Range(0, choices.Length).ToArray();
            random.Shuffle(order);
            await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(file.Text), f.Token);
            for (var step = 0; step < order.Length; step++)
            {
                var shown = await f.ReadAsync();
                var text = await reader.ReadConflictAsync(f.Root, shown.Conflicts.Single(conflict => conflict.Path.Display == "f.txt"), f.Token);
                var block = order[step] - order[..step].Count(taken => taken < order[step]);
                var result = await Write(f, new ConflictBlockRequest(shown, Snapshots.Path("f.txt"), block, choices[order[step]], text.Hash));
                Assert.True(result.Outcome == GitWriteOutcome.Succeeded, $"seed {seed}, step {step}: {result.Message}");
            }
            var written = await File.ReadAllBytesAsync(path, f.Token);
            Assert.True(Encoding.UTF8.GetBytes(file.Resolved(choices)).SequenceEqual(written), $"seed {seed}");
        }
    }

    private static Task<GitWriteResult> Write(GitFixture f, GitWriteRequest request) => new GitWriteRouter(f.Runner).ExecuteAsync(request, f.Token);
    private static string Read(GitFixture f, string path) => File.ReadAllText(Path.Combine(f.Root, path));

    // The writes of the reopen test without its checks: g.txt resolved with Mine, then its conflict brought back.
    private static async Task ResolveAndReopen(GitFixture f)
    {
        await Write(f, new ConflictSideRequest(await f.ReadAsync(), Snapshots.Path("g.txt"), ConflictChoice.Mine));
        await Write(f, new ReopenConflictRequest(await f.ReadAsync(), Snapshots.Path("g.txt")));
    }

    [Fact]
    public async Task WhenASideIsChosenForAConflictedFile_ShouldResolveTheWholeFileAndRecordWhichSideItWas()
    {
        using var f = await Conflicted();
        var before = await f.ReadAsync();
        Assert.Equal([("d.txt", null), ("f.txt", (int?)1), ("g.txt", 1)],
            before.Resolutions.Open.Select(open => (open.Path.Display, open.Blocks)).OrderBy(open => open.Display));
        var theirs = await Write(f, new ConflictSideRequest(before, Snapshots.Path("g.txt"), ConflictChoice.Theirs));
        Assert.True(theirs.Outcome == GitWriteOutcome.Succeeded, theirs.Message + theirs.Details);
        Assert.Equal("x-feature\n", Read(f, "g.txt"));
        var deleted = await Write(f, new ConflictSideRequest(await f.ReadAsync(), Snapshots.Path("d.txt"), ConflictChoice.Theirs));
        Assert.True(deleted.Outcome == GitWriteOutcome.Succeeded, deleted.Message + deleted.Details);
        Assert.False(File.Exists(Path.Combine(f.Root, "d.txt")));
        var mine = await Write(f, new ConflictSideRequest(await f.ReadAsync(), Snapshots.Path("f.txt"), ConflictChoice.Mine));
        Assert.True(mine.Outcome == GitWriteOutcome.Succeeded, mine.Message + mine.Details);

        var after = await f.ReadAsync();
        Assert.Empty(after.Conflicts);
        Assert.Equal([("d.txt", Resolution.Theirs), ("f.txt", Resolution.Mine), ("g.txt", Resolution.Theirs)],
            after.Resolutions.Resolved.Select(path => (path.Path.Display, path.How)).OrderBy(path => path.Display));
        Assert.Equal(["d.txt", "g.txt"], MergeFiles.From(after).Resolved.Where(file => file.Staged is not null).Select(file => file.Path.Display));
    }

    [Fact]
    public async Task WhenConflictBlocksAreChosen_ShouldRewriteOnlyTheBytesTheyWereChosenOnAndKeepTheFileOpenWhileMarkersRemain()
    {
        using var f = await Conflicted();
        var before = await f.ReadAsync();
        var text = await new CliFileDiffReader(f.Runner).ReadConflictAsync(f.Root, before.Conflicts.Single(file => file.Path.Display == "f.txt"), f.Token);
        Assert.True(text.IsReadable);
        var stale = await Write(f, new ConflictBlockRequest(before, Snapshots.Path("f.txt"), 0, ConflictChoice.Both, "0000"));
        Assert.Equal(GitWriteOutcome.Rejected, stale.Outcome);

        var refused = await Write(f, new StageRequest(before, [Snapshots.Path("f.txt")], Resolution: true));
        Assert.Equal(GitWriteOutcome.Rejected, refused.Outcome);
        Assert.StartsWith("f.txt still contains conflict markers.", refused.Message);

        var both = await Write(f, new ConflictBlockRequest(before, Snapshots.Path("f.txt"), 0, ConflictChoice.Both, text.Hash));
        Assert.True(both.Outcome == GitWriteOutcome.Succeeded, both.Message + both.Details);
        Assert.Equal("a\nB-main\nB-feature\nc\n", Read(f, "f.txt"));
        var after = await f.ReadAsync();
        Assert.Equal(0, after.Resolutions.Open.Single(open => open.Path.Display == "f.txt").Blocks);
        var staged = await Write(f, new StageRequest(after, [Snapshots.Path("f.txt")], Resolution: true));
        Assert.True(staged.Outcome == GitWriteOutcome.Succeeded, staged.Message + staged.Details);
        Assert.Equal(Resolution.Edited, (await f.ReadAsync()).Resolutions.Resolved.Single(path => path.Path.Display == "f.txt").How);
    }

    // Conflicts resolved by hand in large files, such as regenerated lockfiles: one too large to look into for markers is
    // not read at all, and the others are read in batches, so together they never take the read over the runner's limit.
    [Fact]
    public async Task WhenConflictsInLargeFilesAreResolvedByHand_ShouldReadTheirResolutionsWithinTheRunnersLimit()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        var sizes = new Dictionary<string, int> { ["a.txt"] = ConflictText.ByteLimit * 3 / 5, ["b.txt"] = ConflictText.ByteLimit * 3 / 5, ["lock.txt"] = ConflictText.ByteLimit };
        async Task CommitSide(string side)
        {
            foreach (var (name, size) in sizes) await f.WriteAsync(name, new string('x', size) + "\n" + side + "\n");
            await f.CommitAsync(side, [.. sizes.Keys]);
        }
        await CommitSide("base");
        await f.GitAsync("switch", "-c", "feature");
        await CommitSide("feature");
        await f.GitAsync("switch", "main");
        await CommitSide("main");
        Assert.NotEqual(0, (await f.RunAsync("merge", "--no-ff", "--no-edit", "feature")).ExitCode);
        foreach (var (name, size) in sizes) await f.WriteAsync(name, new string('x', size) + "\nregenerated\n");
        await f.WriteAsync("b.txt", new string('x', sizes["b.txt"]) + "\n<<<<<<< ours\nmain\n=======\nfeature\n>>>>>>> theirs\n");
        await f.GitAsync(["add", .. sizes.Keys]);

        var snapshot = await new CliRepositoryReader(new GitProcessRunner(outputLimit: ConflictText.ByteLimit)).ReadAsync(f.Root, f.Token);

        Assert.Equal([("a.txt", Resolution.Edited, false), ("b.txt", Resolution.Edited, true), ("lock.txt", Resolution.Edited, false)],
            snapshot.Resolutions.Resolved.Select(path => (path.Path.Display, path.How, path.HasMarkers)).OrderBy(path => path.Display));
    }

    // An editor holding the conflicted file open for writing, as many do while it is shown, does not stop the read.
    [Fact]
    public async Task WhenAnEditorHoldsAConflictedFileOpen_ShouldStillReadItsBlocks()
    {
        using var f = await Conflicted();
        using (new FileStream(Path.Combine(f.Root, "f.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            Assert.Equal(1, (await f.ReadAsync()).Resolutions.Open.Single(open => open.Path.Display == "f.txt").Blocks);
    }

    [Fact]
    public async Task WhenAResolvedConflictIsReopened_ShouldBringTheConflictAndItsMarkersBack()
    {
        using var f = await Conflicted();
        var side = await Write(f, new ConflictSideRequest(await f.ReadAsync(), Snapshots.Path("g.txt"), ConflictChoice.Mine));
        Assert.Equal(GitWriteOutcome.Succeeded, side.Outcome);
        var reopened = await Write(f, new ReopenConflictRequest(await f.ReadAsync(), Snapshots.Path("g.txt")));
        Assert.True(reopened.Outcome == GitWriteOutcome.Succeeded, reopened.Message + reopened.Details);
        Assert.Contains((await f.ReadAsync()).Conflicts, file => file.Path.Display == "g.txt");
        Assert.Contains("<<<<<<<", Read(f, "g.txt"));
    }

    // g.txt carries markers again because the conflict reopened in the test above comes first, as it did before the split.
    [Fact]
    public async Task WhenFilesWithMarkersAreStagedOutsideTrag_ShouldNameThemAndRefuseToFinish()
    {
        using var f = await Conflicted();
        await ResolveAndReopen(f);

        await f.GitAsync("add", "f.txt", "g.txt");
        await f.GitAsync("rm", "-q", "d.txt");
        var marked = await f.ReadAsync();
        Assert.Empty(marked.Conflicts);
        Assert.Equal(["f.txt", "g.txt"], marked.Resolutions.Resolved.Where(path => path.HasMarkers).Select(path => path.Path.Display).Order());
        var finish = await Write(f, new CommitRequest(marked, "Merge feature", FinishesMerge: true));
        Assert.Equal(GitWriteOutcome.Rejected, finish.Outcome);
        Assert.Equal("Finish would commit conflict markers in f.txt, g.txt. Mark the file as unresolved and resolve it again.", finish.Message);
        Assert.Equal(RepositoryOperation.Merge, (await f.ReadAsync()).Operation);
    }

    // Text Git merges with markers and the merge view cannot show: Latin-1, UTF-16 the repository checks out as such
    // (working-tree-encoding), and a file over the view's limit.
    [Theory]
    [InlineData("latin1")]
    [InlineData("utf16")]
    [InlineData("large")]
    public async Task WhenAConflictedFileIsLatin1Utf16OrLargerThanTwoMegabytes_ShouldNotMarkItResolvedWithMarkers(string kind)
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        if (kind == "utf16") await f.WriteAsync(".gitattributes", "c.txt working-tree-encoding=UTF-16\n");
        await WriteSideAsync(f, kind, "base"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "feature");
        await WriteSideAsync(f, kind, "feature"); await f.CommitAsync("Feature");
        await f.GitAsync("switch", "main");
        await WriteSideAsync(f, kind, "main"); await f.CommitAsync("Main");
        Assert.NotEqual(0, (await f.RunAsync("merge", "--no-ff", "--no-edit", "feature")).ExitCode);

        var conflicted = await f.ReadAsync();
        Assert.Equal(1, Assert.Single(conflicted.Resolutions.Open).Blocks);
        var staged = await Write(f, new StageRequest(conflicted, [Snapshots.Path("c.txt")], Resolution: true));
        Assert.Equal(ConflictOutcomeText.MarkersLeft("c.txt"), staged.Message);
        await f.GitAsync("add", "c.txt");
        var finish = await Write(f, new CommitRequest(await f.ReadAsync(), "Merge feature", FinishesMerge: true));
        Assert.Equal("Finish would commit conflict markers in c.txt. Mark the file as unresolved and resolve it again.", finish.Message);
    }

    private static Task WriteSideAsync(GitFixture f, string kind, string side)
    {
        var lines = "a\nb " + side + " ä\nc\n";
        var bytes = kind switch
        {
            "latin1" => Encoding.Latin1.GetBytes(lines),
            "utf16" => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(lines)],
            _ => Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("padding line\n", 200_000)) + lines)
        };
        return File.WriteAllBytesAsync(Path.Combine(f.Root, "c.txt"), bytes, f.Token);
    }

    // main renamed a.txt to b.txt and feature changed it: Git keeps the conflict under main's name, with feature's side
    // taken from a.txt, a name the incoming commit has and main does not.
    [Fact]
    public async Task WhenOursRenamedAndTheirsModified_ShouldOfferReopenAndRefuseMarkersStagedOutside()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("a.txt", "one\ntwo\nthree\nfour\nfive\nsix\n"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "feature");
        await f.WriteAsync("a.txt", "one\ntwo\nthree-feature\nfour\nfive\nsix\n"); await f.CommitAsync("Feature");
        await f.GitAsync("switch", "main");
        await f.GitAsync("mv", "a.txt", "b.txt"); await f.WriteAsync("b.txt", "one\ntwo\nthree-main\nfour\nfive\nsix\n"); await f.CommitAsync("Main");
        Assert.NotEqual(0, (await f.RunAsync("merge", "--no-ff", "--no-edit", "feature")).ExitCode);
        Assert.Equal(["b.txt"], (await f.ReadAsync()).Conflicts.Select(file => file.Path.Display));

        await f.GitAsync("add", "b.txt");
        var marked = await f.ReadAsync();
        var resolved = Assert.Single(marked.Resolutions.Resolved);
        Assert.Equal(("b.txt", Resolution.Edited, true), (resolved.Path.Display, resolved.How, resolved.HasMarkers));
        var finish = await Write(f, new CommitRequest(marked, "Merge feature", FinishesMerge: true));
        Assert.Equal("Finish would commit conflict markers in b.txt. Mark the file as unresolved and resolve it again.", finish.Message);
        var reopened = await Write(f, new ReopenConflictRequest(await f.ReadAsync(), Snapshots.Path("b.txt")));
        Assert.True(reopened.Outcome == GitWriteOutcome.Succeeded, reopened.Message + reopened.Details);
        Assert.Contains((await f.ReadAsync()).Conflicts, file => file.Path.Display == "b.txt");
    }

    [Fact]
    public async Task WhenTheRepositoryUsesLongerConflictMarkers_ShouldCountTheConflictsBlock()
    {
        using var f = await ConflictedWithLongMarkers();

        var before = await f.ReadAsync();

        Assert.Equal(1, before.Resolutions.Open.Single(open => open.Path.Display == "guide.adoc").Blocks);
    }

    [Fact]
    public async Task WhenTheRepositoryUsesLongerConflictMarkers_ShouldRefuseToMarkTheFileResolvedWhileTheyRemain()
    {
        using var f = await ConflictedWithLongMarkers();

        var staged = await Write(f, new StageRequest(await f.ReadAsync(), [Snapshots.Path("guide.adoc")], Resolution: true));

        Assert.Equal(GitWriteOutcome.Rejected, staged.Outcome);
        Assert.Contains((await f.ReadAsync()).Conflicts, file => file.Path.Display == "guide.adoc");
    }

    [Fact]
    public async Task WhenAFileWithLongerMarkersIsStagedOutsideTrag_ShouldRefuseToFinishTheMerge()
    {
        using var f = await ConflictedWithLongMarkers();
        await f.GitAsync("add", "guide.adoc");
        var marked = await f.ReadAsync();
        Assert.Empty(marked.Conflicts);

        var finish = await Write(f, new CommitRequest(marked, "Merge feature", FinishesMerge: true));

        Assert.Equal(GitWriteOutcome.Rejected, finish.Outcome);
        Assert.Equal(RepositoryOperation.Merge, (await f.ReadAsync()).Operation);
    }

    [Fact]
    public async Task WhenAMergeIsFinishedAndAnotherOneConflicts_ShouldReadNoResolutionsBetweenThemAndNoneOfTheOlderOnesInTheNewOne()
    {
        using var f = await Conflicted();
        await Write(f, new ConflictSideRequest(await f.ReadAsync(), Snapshots.Path("g.txt"), ConflictChoice.Theirs));
        await Write(f, new ConflictSideRequest(await f.ReadAsync(), Snapshots.Path("f.txt"), ConflictChoice.Theirs));
        await Write(f, new ConflictSideRequest(await f.ReadAsync(), Snapshots.Path("d.txt"), ConflictChoice.Mine));
        var done = await Write(f, new CommitRequest(await f.ReadAsync(), "Merge feature", FinishesMerge: true));
        Assert.True(done.Outcome == GitWriteOutcome.Succeeded, done.Message + done.Details);
        Assert.Same(MergeResolutions.None, (await f.ReadAsync()).Resolutions);

        // A second merge that conflicts only on a new file: the old resolve-undo records stay out of its list.
        await f.GitAsync("switch", "-c", "second", "HEAD~1");
        await f.WriteAsync("n.txt", "second\n"); await f.CommitAsync("Second");
        await f.GitAsync("switch", "main");
        await f.WriteAsync("n.txt", "main\n"); await f.CommitAsync("Main n");
        Assert.NotEqual(0, (await f.RunAsync("merge", "--no-ff", "--no-edit", "second")).ExitCode);
        var second = await f.ReadAsync();
        Assert.Empty(second.Resolutions.Resolved);
        Assert.Equal("n.txt", Encoding.UTF8.GetString(Assert.Single(second.Resolutions.Open).Path.Bytes));
    }
}
