using Trag.Domain.Merge;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Merge;

// The two lists of a merge in progress, how each file is described, and what the banner, Finish and the sidebar say.
public sealed class MergeFilesTests
{
    private static RepositorySnapshot Merge(MergeResolutions resolutions, params FileStatus[] files) => Snapshots.Create(branch: "PPDB-1229",
        operation: RepositoryOperation.Merge, files: files, merge: new(Snapshots.Id('b'), Snapshots.HeadId, "Merge remote-tracking branch 'origin/dotnet8.0' into PPDB-1229\n\n# Conflicts:\n#\ta.csproj\n", "m", ""))
        with { Resolutions = resolutions };

    [Fact]
    public void WhenAMergeStops_ShouldListItsConflictsWithTheBlocksLeftAndEveryDecidedFileWithMarkersFirst()
    {
        var files = MergeFiles.From(StoppedMerge());
        Assert.Equal([("a.csproj", null), ("b.csproj", (int?)3)], files.Conflicts.Select(file => (file.File.Path.Display, file.Blocks)));
        Assert.Equal(["z.csproj", "auto.txt", "mine.txt", "theirs.txt"], files.Resolved.Select(file => file.Path.Display));
        Assert.Equal(6, files.Total);
    }

    [Fact]
    public void WhenAMergeStops_ShouldTagEachDecidedFileWithHowItWasDecided()
    {
        var snapshot = StoppedMerge();
        var files = MergeFiles.From(snapshot);
        Assert.Equal(["markers left", "auto", "mine", "theirs"], files.Resolved.Select(file => MergeText.Tag(file, ConflictSides.For(snapshot))));
    }

    [Fact]
    public void WhenMineLeavesAFileAsOnTheBranch_ShouldListItWithNothingStaged()
    {
        var files = MergeFiles.From(StoppedMerge());
        Assert.Null(files.Resolved.Single(file => file.How == Resolution.Mine).Staged);
    }

    [Fact]
    public void WhenGitMergedAFileOnItsOwn_ShouldNotLetItComeBackAsAConflict()
    {
        var files = MergeFiles.From(StoppedMerge());
        Assert.False(files.Resolved.Single(file => file.How == Resolution.Automatic).CanReopen);
    }

    [Fact]
    public void WhenNoMergeIsInProgress_ShouldListNoFiles()
    {
        Assert.Same(MergeFiles.Empty, MergeFiles.From(Snapshots.Create(files: [Snapshots.Staged("x")])));
    }

    [Fact]
    public void WhenConflictsAreLeft_ShouldSayHowManyAndKeepFinishBack()
    {
        var conflicted = Merge(MergeResolutions.None, Snapshots.Conflict("a.csproj"), Snapshots.Staged("auto.txt"));
        var open = new MergeOverview(conflicted, MergeFiles.From(conflicted));
        Assert.Equal("origin/dotnet8.0 → PPDB-1229", open.Direction);
        Assert.Equal("1 of 2 files is still in conflict.", open.Remaining);
        Assert.Equal(("1 of 2 resolved", 50.0), (open.ProgressText, open.Progress));
        Assert.Equal("Finish is available once every conflict is resolved · 1 left", open.FinishHint);
        Assert.Equal(("1", "1 file is still in conflict. Conflicts are resolved in the merge view, not here."), (open.SidebarCount, open.FileStatusBanner));
        Assert.True(open.IsBlocked);
    }

    [Fact]
    public void WhenADecidedFileStillHasMarkers_ShouldNameItAndKeepFinishBack()
    {
        var marked = Merge(new([new(Snapshots.Path("a.csproj"), Resolution.Edited, HasMarkers: true)], []), Snapshots.Staged("a.csproj"));
        var markers = new MergeOverview(marked, MergeFiles.From(marked));
        Assert.Equal("1 of 1 resolved file still contains conflict markers.", markers.Remaining);
        Assert.Equal("a.csproj still contains <<<<<<< / >>>>>>> lines.", markers.MarkersText);
        Assert.Equal("Blocked: a staged file still has conflict markers", markers.FinishHint);
        Assert.True(markers.IsBlocked); Assert.Equal(("1", "1 file still has conflict markers"), (markers.SidebarCount, markers.SidebarTip));
    }

    [Fact]
    public void WhenManyDecidedFilesStillHaveMarkers_ShouldNameTheFirstThreeAndCountTheRest()
    {
        var names = Enumerable.Range(1, 5).Select(i => "f" + i + ".cs").ToArray();
        var five = Merge(new(names.Select(name => new ResolvedPath(Snapshots.Path(name), Resolution.Edited, HasMarkers: true)).ToArray(), []),
            names.Select(name => Snapshots.Staged(name)).ToArray());
        Assert.Equal("5 files still contain <<<<<<< / >>>>>>> lines: f1.cs, f2.cs, f3.cs and 2 more.", new MergeOverview(five, MergeFiles.From(five)).MarkersText);
    }

    [Fact]
    public void WhenEveryConflictIsResolved_ShouldLetFinishGoAhead()
    {
        var done = Merge(new([new(Snapshots.Path("a.csproj"), Resolution.Theirs)], []), Snapshots.Staged("a.csproj"));
        var ready = new MergeOverview(done, MergeFiles.From(done));
        Assert.False(ready.IsBlocked);
        Assert.Equal("All conflicts are resolved. Review the result and finish.", ready.Remaining);
    }

    [Fact]
    public void WhenGitListedTheConflictsInTheMessage_ShouldLeaveTheListOut()
    {
        Assert.Equal("Merge remote-tracking branch 'origin/dotnet8.0' into PPDB-1229",
            MergeMessages.Clean("Merge remote-tracking branch 'origin/dotnet8.0' into PPDB-1229\r\n\r\n# Conflicts:\r\n#\ta.csproj\r\n"));
    }

    // Only Git's list goes: a line of the message that starts with '#', an issue number, is the user's, and so is a line
    // that ends like the list's heading with no paths after it.
    [Fact]
    public void WhenTheMessagesOwnLinesStartWithAHash_ShouldKeepThemAndLeaveOnlyGitsListOut()
    {
        Assert.Equal("#123 Fix\n\n#124 kept", MergeMessages.Clean("#123 Fix\n\n#124 kept\n\n# Conflicts:\n#\tf.txt\n#\tg.txt\n"));
        Assert.Equal("#123 Fix\n\nResolve Conflicts:", MergeMessages.Clean("#123 Fix\n\nResolve Conflicts:\n"));
        Assert.Equal("Resolve Conflicts:\n\ttaking theirs", MergeMessages.Clean("Resolve Conflicts:\n\ttaking theirs\n"));
    }

    // Git writes the list with core.commentChar, and after its scissors when commit.cleanup is scissors.
    [Fact]
    public void WhenGitListedTheConflictsWithAnotherCommentCharOrAfterItsScissors_ShouldLeaveThatOutToo()
    {
        Assert.Equal("Merge branch 'side'", MergeMessages.Clean("Merge branch 'side'\n\n; Conflicts:\n;\tf.txt\n"));
        Assert.Equal("#12 side", MergeMessages.Clean("#12 side\n\n# ------------------------ >8 ------------------------\n# Do not modify or remove the line above.\n"
            + "# Everything below it will be ignored.\n#\n# Conflicts:\n#\tf.txt\n"));
    }

    [Fact]
    public void WhenTheMessageHasNoConflictList_ShouldKeepItsLines()
    {
        Assert.Equal("Subject\n\nBody", MergeMessages.Clean("Subject\n\nBody\n"));
    }

    [Fact]
    public void WhenGitsMessageMergesARemoteTrackingBranch_ShouldNameThatBranchAsIncoming()
    {
        Assert.Equal("origin/dotnet8.0", MergeMessages.IncomingName(MergeOf("Merge remote-tracking branch 'origin/dotnet8.0' into x", null)));
    }

    [Fact]
    public void WhenGitsMessageMergesALocalBranch_ShouldNameThatBranchAsIncoming()
    {
        Assert.Equal("topic", MergeMessages.IncomingName(MergeOf("Merge branch 'topic'", null)));
    }

    [Fact]
    public void WhenTheMessageNamesNoBranch_ShouldNameTheIncomingCommitByItsShortId()
    {
        Assert.Equal("bbbbbbb", MergeMessages.IncomingName(MergeOf("My own words", Snapshots.Id('b'))));
    }

    [Fact]
    public void WhenAMergeStops_ShouldNameTheSidesAfterTheirBranches()
    {
        var sides = ConflictSides.For(Merge(MergeResolutions.None));
        Assert.Equal(("MINE · PPDB-1229", "THEIRS · origin/dotnet8.0", "the merge"), (sides.Yours.Title, sides.Other.Title, sides.Change));
    }

    [Fact]
    public void WhenTheirSideDeletedAFileMineChanged_ShouldSaySoAndOfferTheirsToDeleteIt()
    {
        var sides = ConflictSides.For(Merge(MergeResolutions.None));
        var byTheirs = new MergeConflict(new(Snapshots.Path("Legacy.cs"), 'U', 'D', IsConflict: true), null);
        Assert.Equal("Conflict · origin/dotnet8.0 deleted this file, PPDB-1229 changed it", MergeText.ConflictLine(byTheirs, sides));
        Assert.EndsWith("or Use theirs to delete the file, as origin/dotnet8.0 did.", MergeText.DeletionChoice(byTheirs, sides));
    }

    [Fact]
    public void WhenMySideDeletedAFileTheirsChanged_ShouldSaySoAndOfferMineToDeleteIt()
    {
        var sides = ConflictSides.For(Merge(MergeResolutions.None));
        var byMine = new MergeConflict(new(Snapshots.Path("Legacy.cs"), 'D', 'U', IsConflict: true), null);
        Assert.Equal("Conflict · PPDB-1229 deleted this file, origin/dotnet8.0 changed it", MergeText.ConflictLine(byMine, sides));
        Assert.StartsWith("Use mine to delete the file, as PPDB-1229 did", MergeText.DeletionChoice(byMine, sides));
    }

    // Renamed apart on both sides, the old name is gone from both (DD): neither side changed it, and either deletes it.
    [Fact]
    public void WhenBothSidesDeletedAFile_ShouldSaySoAndDeleteItWithEitherSide()
    {
        var sides = ConflictSides.For(Merge(MergeResolutions.None));
        var both = new MergeConflict(new(Snapshots.Path("Legacy.cs"), 'D', 'D', IsConflict: true), null);
        Assert.True(both.IsDeletion);
        Assert.Equal("Conflict · PPDB-1229 and origin/dotnet8.0 both deleted this file", MergeText.ConflictLine(both, sides));
        Assert.Equal("Use mine or theirs to delete the file, as both sides did.", MergeText.DeletionChoice(both, sides));
    }

    // Renamed apart, each new name exists on one side only (AU, UA): a question of keeping it or not, never text blocks.
    [Fact]
    public void WhenOnlyOneSideHasTheFile_ShouldAskWhetherToKeepItAndSayTheOtherSideDeletesIt()
    {
        var sides = ConflictSides.For(Merge(MergeResolutions.None));
        var oursOnly = new MergeConflict(new(Snapshots.Path("Mine.cs"), 'A', 'U', IsConflict: true), 0);
        var theirsOnly = new MergeConflict(new(Snapshots.Path("Theirs.cs"), 'U', 'A', IsConflict: true), 0);
        Assert.True(oursOnly.IsDeletion); Assert.True(theirsOnly.IsDeletion);
        Assert.Equal("Conflict · PPDB-1229 added this file, origin/dotnet8.0 has none", MergeText.ConflictLine(oursOnly, sides));
        Assert.Equal("Use mine to keep the version from PPDB-1229, or Use theirs to delete the file, as origin/dotnet8.0 has none.", MergeText.DeletionChoice(oursOnly, sides));
        Assert.Equal("Conflict · origin/dotnet8.0 added this file, PPDB-1229 has none", MergeText.ConflictLine(theirsOnly, sides));
        Assert.Equal("Use mine to delete the file, as PPDB-1229 has none, or Use theirs to keep the version from origin/dotnet8.0.", MergeText.DeletionChoice(theirsOnly, sides));
    }

    [Fact]
    public void WhenBlocksAreLeftInAConflict_ShouldCountThemAndNameTheSides()
    {
        var sides = ConflictSides.For(Merge(MergeResolutions.None));
        Assert.Equal("Conflict · 2 blocks left · mine = PPDB-1229 · theirs = origin/dotnet8.0",
            MergeText.ConflictLine(new(Snapshots.Conflict("a.csproj"), 2), sides));
    }

    [Fact]
    public void WhenNoBlockIsLeftInAConflict_ShouldSaySoAndNameTheSides()
    {
        var sides = ConflictSides.For(Merge(MergeResolutions.None));
        Assert.Equal("Conflict · no blocks left · mine = PPDB-1229 · theirs = origin/dotnet8.0",
            MergeText.ConflictLine(new(Snapshots.Conflict("a.csproj"), 0), sides));
    }

    [Fact]
    public void WhenAConflictIsResolvedByDeletingTheFile_ShouldSaySo()
    {
        var sides = ConflictSides.For(Merge(MergeResolutions.None));
        var deleted = new MergeResolved(Snapshots.Path("Legacy.cs"), Resolution.Theirs, false, Snapshots.Staged("Legacy.cs", 'D'));
        Assert.StartsWith("Resolved by deleting the file.", MergeText.ResolvedLine(deleted, sides));
    }

    [Fact]
    public void WhenAMergeHasConflicts_ShouldCountThemApartOnTheStatusLine()
    {
        var snapshot = Merge(MergeResolutions.None, Snapshots.Conflict("a.csproj"), Snapshots.Conflict("b.csproj"), Snapshots.Staged("auto.txt"), Snapshots.Modified("notes.md"));
        Assert.Equal("1 staged · 1 unstaged · 2 conflicts", StatusText.Line(snapshot));
    }

    [Fact]
    public void WhenNoFileIsInConflict_ShouldNotMentionConflictsOnTheStatusLine()
    {
        Assert.Equal("0 staged · 1 unstaged", StatusText.Line(Snapshots.Create(files: [Snapshots.Modified("a")])));
    }

    [Fact]
    public void WhenTheWorkingTreeIsClean_ShouldSaySoOnTheStatusLine()
    {
        Assert.Equal("Working tree clean", StatusText.Line(Snapshots.Create()));
    }

    [Fact]
    public void WhenAFileMarkedResolvedIsTheSameAsOnTheBranch_ShouldSayThereIsNothingToStage()
    {
        var (mark, unchanged, _) = DockerfileResolved();
        Assert.Equal("Dockerfile is resolved. It is the same as on PPDB-1229, so there is nothing to stage for it.", ResolutionOutcome.Described(mark, Staged(unchanged)).Message);
    }

    // A file the merge staged on its own is not the file marked resolved.
    [Fact]
    public void WhenAFileMarkedResolvedIsTheSameAsOnTheBranchBesideAnotherStagedFile_ShouldSayThereIsNothingToStageForIt()
    {
        var (mark, _, _) = DockerfileResolved();
        var after = Merge(MergeResolutions.None, Snapshots.Conflict("a.csproj"), Snapshots.Staged("auto.txt"));
        Assert.Equal("Dockerfile is resolved. It is the same as on PPDB-1229, so there is nothing to stage for it.", ResolutionOutcome.Described(mark, Staged(after)).Message);
    }

    [Fact]
    public void WhenAFileMarkedResolvedDiffersFromTheBranch_ShouldSayItIsStaged()
    {
        var (mark, _, changed) = DockerfileResolved();
        Assert.Equal("Dockerfile is resolved and staged.", ResolutionOutcome.Described(mark, Staged(changed)).Message);
    }

    [Fact]
    public void WhenASideLeavesTheFileAsOnTheBranch_ShouldSayThereIsNothingToStage()
    {
        var (_, unchanged, _) = DockerfileResolved();
        var mine = new ConflictSideRequest(Snapshots.Create(), Snapshots.Path("Dockerfile"), ConflictChoice.Mine);
        Assert.Equal("Dockerfile resolved with mine. It is the same as on PPDB-1229, so there is nothing to stage for it.",
            ResolutionOutcome.Described(mine, Staged(unchanged, "Dockerfile resolved with mine.")).Message);
    }

    [Fact]
    public void WhenASideChangesTheFile_ShouldKeepTheWritesOwnSentence()
    {
        var (_, _, changed) = DockerfileResolved();
        var mine = new ConflictSideRequest(Snapshots.Create(), Snapshots.Path("Dockerfile"), ConflictChoice.Mine);
        Assert.Equal("Dockerfile resolved with theirs.", ResolutionOutcome.Described(mine with { Side = ConflictChoice.Theirs }, Staged(changed, "Dockerfile resolved with theirs.")).Message);
    }

    [Fact]
    public void WhenMarkingResolvedIsRefused_ShouldKeepTheRefusalsOwnSentence()
    {
        var (mark, _, _) = DockerfileResolved();
        Assert.Equal("No.", ResolutionOutcome.Described(mark, GitWriteResult.Rejected("No.")).Message);
    }

    // Without the state after it, there is no telling whether anything was staged.
    [Fact]
    public void WhenAResultCarriesNoStateAfterIt_ShouldKeepItsOwnSentence()
    {
        var (mark, _, _) = DockerfileResolved();
        Assert.Equal("Selected whole files staged.", ResolutionOutcome.Described(mark, new(GitWriteOutcome.Succeeded, "Selected whole files staged.")).Message);
    }

    [Fact]
    public void WhenAFileIsStagedWithoutMarkingItResolved_ShouldKeepTheWritesOwnSentence()
    {
        var (mark, unchanged, _) = DockerfileResolved();
        Assert.Equal("Selected whole files staged.", ResolutionOutcome.Described(mark with { Resolution = false }, Staged(unchanged)).Message);
    }

    // a.csproj and b.csproj (3 blocks left) still in conflict; z.csproj edited and staged with its markers, theirs.txt
    // taken from theirs, mine.txt left as on the branch by mine, and auto.txt merged by Git on its own.
    private static RepositoryMerge MergeOf(string message, string? incomingHead) => new(incomingHead, null, message, "", "");

    private static RepositorySnapshot StoppedMerge() =>
        Merge(new([new(Snapshots.Path("mine.txt"), Resolution.Mine), new(Snapshots.Path("z.csproj"), Resolution.Edited, HasMarkers: true),
                new(Snapshots.Path("theirs.txt"), Resolution.Theirs)], [new(Snapshots.Path("b.csproj"), 3)]),
            Snapshots.Conflict("b.csproj"), Snapshots.Conflict("a.csproj"), Snapshots.Staged("theirs.txt"), Snapshots.Staged("auto.txt"), Snapshots.Staged("z.csproj"));

    // Marking Dockerfile resolved, and the merge after it: Dockerfile the same as on the branch, or changed and staged.
    private static (StageRequest Mark, RepositorySnapshot Unchanged, RepositorySnapshot Changed) DockerfileResolved() =>
        (new StageRequest(Snapshots.Create(), [Snapshots.Path("Dockerfile")], Resolution: true),
            Merge(MergeResolutions.None, Snapshots.Conflict("a.csproj")),
            Merge(MergeResolutions.None, Snapshots.Conflict("a.csproj"), Snapshots.Staged("Dockerfile")));

    private static GitWriteResult Staged(RepositorySnapshot after, string message = "Selected whole files staged.") => new(GitWriteOutcome.Succeeded, message, Snapshot: after);
}
