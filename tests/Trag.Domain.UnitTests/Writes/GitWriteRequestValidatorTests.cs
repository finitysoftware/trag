using System.Text;
using Trag.Domain.Merge;
using Trag.Domain.Naming;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Rebase;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Writes;

// Everything the router refuses before Git is started, in the words the user reads.
public sealed class GitWriteRequestValidatorTests
{
    private static GitPath Path(string value) => new(Encoding.UTF8.GetBytes(value));

    private static RepositorySnapshot Expected(IReadOnlyList<FileStatus>? files = null) =>
        Snapshots.Create(files: files ?? [Snapshots.Modified("one.txt")]);

    private static string? Validate(GitWriteRequest request) => GitWriteRequestValidator.Validate(request);

    public static TheoryData<GitWriteRequest> NothingToValidate => new()
    {
        new StashRequest(Expected(), new("keep"), StashAction.Push),
        new StashRequest(Expected(), new(Id: Snapshots.Id('c')), StashAction.Apply),
        new StashRequest(Expected(), new(Id: Snapshots.Id('c')), StashAction.Drop),
        new MergeAbortRequest(Expected()),
        new BranchRequest(Expected(), new("main"), BranchAction.Switch),
        new ResetRequest(Expected(), Snapshots.Id('b'), ResetMode.Hard)
    };

    [Theory]
    [MemberData(nameof(NothingToValidate))]
    public void WhenARequestCannotBeBuiltWrong_ShouldHaveNothingToRefuse(GitWriteRequest request) => Assert.Null(Validate(request));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("with\0nul")]
    public void WhenTheCommitMessageIsBlankOrHasANul_ShouldAskForAMessageWithinTheRules(string message) =>
        Assert.Equal("Enter a commit message of at most 64 KB without NUL characters.",
            Validate(new CommitRequest(Expected([Snapshots.Staged("one.txt")]), message)));

    [Fact]
    public void WhenTheCommitMessageIsOverSixtyFourKilobytes_ShouldAskForAMessageWithinTheRules() =>
        Assert.Equal("Enter a commit message of at most 64 KB without NUL characters.",
            Validate(new CommitRequest(Expected([Snapshots.Staged("one.txt")]), new string('a', 64 * 1024 + 1))));

    // A signed tag is an annotated one, and Git asks for a missing message in an editor Trag cannot show.
    [Fact]
    public void WhenTagsAreSignedAndTheTagHasNoMessage_ShouldAskForAMessage()
    {
        var signed = Expected() with { SignsTags = true };

        Assert.Equal("Tags are signed in this repository, so a tag needs a message.", Validate(new TagRequest(signed, new("v1", Snapshots.Id('a'), " \n"))));
        Assert.Null(Validate(new TagRequest(signed, new("v1", Snapshots.Id('a'), "Release"))));
        Assert.Null(Validate(new TagRequest(Expected(), new("v1", Snapshots.Id('a')))));
    }

    // Co-authors alone have no subject above them, and a commit button would not offer them either.
    [Fact]
    public void WhenTheMessageIsOnlyCoAuthors_ShouldAskForTheSubjectAboveThem()
    {
        var message = "\n\nCo-authored-by: Ana Anić <ana@example.invalid>";

        Assert.Equal("Write the commit's subject above its Co-authored-by lines.", Validate(new CommitRequest(Expected([Snapshots.Staged("one.txt")]), message)));
        Assert.False(CommitRequest.IsValidMessage(message));
    }

    [Fact]
    public void WhenNothingIsStaged_ShouldAskToStageAFileBeforeCommitting() =>
        Assert.Equal("Stage at least one file before committing.", Validate(new CommitRequest(Expected(), "subject")));

    [Fact]
    public void WhenACommitFinishesAMergeWithNothingStaged_ShouldAllowIt() =>
        Assert.Null(Validate(new CommitRequest(Expected(), "subject", FinishesMerge: true)));

    // The same rules the Remotes dialog asks, asked again of each remote request.
    [Fact]
    public void WhenARemoteRequestBreaksTheRemoteRules_ShouldRefuseItInTheDialogsWords()
    {
        var expected = Snapshots.Create(remotes: [Snapshots.Remote()]);
        Assert.Equal("A remote named origin already exists. Choose another name.", Validate(new RemoteAddRequest(expected, "origin", "https://example.com/x.git")));
        Assert.Null(Validate(new RemoteAddRequest(expected, "fork", "https://example.com/x.git")));
        Assert.Equal("Enter the remote's URL or path.", Validate(new RemoteEditRequest(expected, "origin", "origin", "")));
        Assert.Null(Validate(new RemoteEditRequest(expected, "origin", "main-remote", null)));
        Assert.Equal("That remote is no longer in this repository. Refresh and try again.", Validate(new RemoteRemoveRequest(expected, "fork")));
        Assert.Null(Validate(new RemoteRemoveRequest(expected, "origin")));
    }

    // The pattern becomes one line of .gitignore, so it can be neither empty nor hold a line break.
    [Theory]
    [InlineData("")]
    [InlineData("/a\nb")]
    [InlineData("/a\r")]
    public void WhenAnIgnoreLineIsEmptyOrBreaksTheLine_ShouldRefuseIt(string pattern) =>
        Assert.Equal(IgnoreText.LineInvalid, Validate(new IgnoreRequest(Expected(), Snapshots.Path("a.txt"), pattern)));

    // The commit a file is restored from, or whose changes to it are undone, goes onto Git's command line, so it must be an
    // object id.
    [Fact]
    public void WhenARestoreNamesSomethingOtherThanACommit_ShouldRefuseIt()
    {
        Assert.Null(Validate(new RestoreFileRequest(Expected(), Snapshots.Id('c'), Snapshots.Path("a.txt"))));
        Assert.Equal(FileVersionText.CommitGone, Validate(new RestoreFileRequest(Expected(), "--staged", Snapshots.Path("a.txt"))));
        Assert.Null(Validate(new RevertFileRequest(Expected(), Snapshots.Id('c'), Snapshots.Path("a.txt"), null)));
        Assert.Equal(FileVersionText.RevertCommitGone, Validate(new RevertFileRequest(Expected(), "--output=x", Snapshots.Path("a.txt"), null)));
    }

    // Restoring a file and undoing a commit's changes to it name the file to Git as text.
    [Fact]
    public void WhenARestoreOrAnUndoNamesAFileThatIsNotText_ShouldRefuseIt()
    {
        var latin = new GitPath([0x63, 0xE8, 0x2E, 0x74]);
        Assert.Equal(FileVersionText.NameNotText, Validate(new RestoreFileRequest(Expected(), Snapshots.Id('c'), latin)));
        Assert.Equal(FileVersionText.NameNotText, Validate(new RevertFileRequest(Expected(), Snapshots.Id('c'), latin, null)));
        Assert.Equal(FileVersionText.NameNotText, Validate(new RevertFileRequest(Expected(), Snapshots.Id('c'), Snapshots.Path("b.txt"), latin)));
    }

    [Fact]
    public void WhenAnIgnoreLineIsOneLine_ShouldAllowIt() =>
        Assert.Null(Validate(new IgnoreRequest(Expected(), Snapshots.Path("a.txt"), "/a.txt")));

    // A rewrite's ids go into Git's plan and onto its command line, so each must be an object id; the plan's rules follow.
    [Fact]
    public void WhenARewriteNamesSomethingOtherThanCommitsOrBreaksAPlanRule_ShouldRefuseIt()
    {
        var expected = Expected();
        static RewriteStep Step(string id, RewriteAction action = RewriteAction.Pick) => new(id, "Subject", "Message", action, "Message");

        Assert.Null(Validate(new RewriteRequest(expected, Snapshots.Id('b'), [Step(Snapshots.Id('c'))])));
        Assert.Null(Validate(new RewriteRequest(expected, null, [Step(Snapshots.Id('c'))])));
        Assert.Equal(RewriteText.Unreadable, Validate(new RewriteRequest(expected, "--exec=x", [Step(Snapshots.Id('c'))])));
        Assert.Equal(RewriteText.Unreadable, Validate(new RewriteRequest(expected, Snapshots.Id('b'), [Step("c\nexec x")])));
        Assert.Equal(RewriteText.Unreadable, Validate(new RewriteRequest(expected, Snapshots.Id('b'), [])));
        Assert.Equal(RewriteText.AllDropped, Validate(new RewriteRequest(expected, Snapshots.Id('b'), [Step(Snapshots.Id('c'), RewriteAction.Drop)])));
    }

    // A hunk's patch is taken only for a file the expected repository has changed on that side; a discard takes it out of
    // the working tree.
    [Fact]
    public void WhenAHunksFileIsNoLongerChangedOnItsSide_ShouldRefuseThePatch()
    {
        var expected = Expected([Snapshots.Modified("one.txt"), Snapshots.Staged("staged.txt")]);
        Assert.Null(Validate(new PatchRequest(expected, Path("one.txt"), "patch", PatchAction.Discard, Lines: false)));
        Assert.Equal("That change is no longer in the file. Refresh and try again.", Validate(new PatchRequest(expected, Path("staged.txt"), "patch", PatchAction.Discard, Lines: false)));
        Assert.Null(Validate(new PatchRequest(expected, Path("one.txt"), "patch", PatchAction.Stage, Lines: false)));
        Assert.Equal("That change is no longer in the file. Refresh and try again.", Validate(new PatchRequest(expected, Path("one.txt"), "patch", PatchAction.Unstage, Lines: false)));
        Assert.Equal("That change is no longer in the file. Refresh and try again.", Validate(new PatchRequest(expected, Path("two.txt"), "patch", PatchAction.Stage, Lines: false)));
    }

    // The commit is handed to Git as an argument, so only an object id gets there.
    [Fact]
    public void WhenAResetNamesNoCommitId_ShouldRefuseIt() =>
        Assert.Equal("The commit to reset to is no longer available. Refresh and choose it again.", Validate(new ResetRequest(Expected(), "--hard", ResetMode.Mixed)));

    // The branch's commit goes onto Git's command line, so it must be an object id, and the message is checked even for
    // a fast-forward, which does not use it.
    [Fact]
    public void WhenAMergeNamesNoCommitOrAMessageOutsideTheRules_ShouldRefuseIt()
    {
        Assert.Null(Validate(new MergeRequest(Expected(), "topic", Snapshots.Id('b'), false, "Merge topic")));
        Assert.Equal(WriteText.MergeBranchMissing, Validate(new MergeRequest(Expected(), "topic", "topic", false, "Merge topic")));
        Assert.Equal(WriteText.MergeMessageInvalid, Validate(new MergeRequest(Expected(), "topic", Snapshots.Id('b'), false, " ")));
    }

    [Fact]
    public void WhenARebaseNamesNoOntoCommit_ShouldAskForABranchToRebaseOnto()
    {
        Assert.Null(Validate(new RebaseRequest(Expected(), "main", Snapshots.Id('b'))));
        Assert.Equal(WriteText.RebaseOntoMissing, Validate(new RebaseRequest(Expected(), "main", "main")));
    }

    // The message Continue commits a stop with keeps the rules of a commit's.
    [Fact]
    public void WhenARebaseContinuesWithAMessageOutsideTheRules_ShouldAskForAMessageWithinTheRules()
    {
        Assert.Null(Validate(new RebaseContinueRequest(Expected(), "Fix the parser")));
        Assert.Equal(WriteText.CommitMessageInvalid, Validate(new RebaseContinueRequest(Expected(), "with\0nul")));
    }

    // A whole file takes one side, and only a file still in conflict takes one.
    [Fact]
    public void WhenASideIsTakenForAWholeFile_ShouldAskForOneSideOfAFileStillInConflict()
    {
        var expected = Expected([Snapshots.Conflict("one.txt"), Snapshots.Modified("two.txt")]);

        Assert.Null(Validate(new ConflictSideRequest(expected, Path("one.txt"), ConflictChoice.Mine)));
        Assert.Equal(WriteText.BothSidesChosen, Validate(new ConflictSideRequest(expected, Path("one.txt"), ConflictChoice.Both)));
        Assert.Equal(WriteText.ConflictedFileMissing, Validate(new ConflictSideRequest(expected, Path("two.txt"), ConflictChoice.Theirs)));
    }

    // Blocks are counted from 0, and the hash of the bytes the choice was made on comes with the block.
    [Fact]
    public void WhenAConflictBlockIsChosen_ShouldAskForABlockOfAFileStillInConflict()
    {
        var expected = Expected([Snapshots.Conflict("one.txt"), Snapshots.Modified("two.txt")]);

        Assert.Null(Validate(new ConflictBlockRequest(expected, Path("one.txt"), 0, ConflictChoice.Both, "f00d")));
        Assert.Equal(WriteText.ConflictBlockMissing, Validate(new ConflictBlockRequest(expected, Path("one.txt"), -1, ConflictChoice.Mine, "f00d")));
        Assert.Equal(WriteText.ConflictBlockMissing, Validate(new ConflictBlockRequest(expected, Path("one.txt"), 0, ConflictChoice.Mine, "")));
        Assert.Equal(WriteText.ConflictedFileMissing, Validate(new ConflictBlockRequest(expected, Path("two.txt"), 0, ConflictChoice.Mine, "f00d")));
    }

    [Fact]
    public void WhenAFileIsReopenedAsAConflict_ShouldRefuseOneStillInConflict()
    {
        var expected = Expected([Snapshots.Conflict("one.txt"), Snapshots.Staged("two.txt")]);

        Assert.Equal(WriteText.AlreadyInConflict, Validate(new ReopenConflictRequest(expected, Path("one.txt"))));
        Assert.Null(Validate(new ReopenConflictRequest(expected, Path("two.txt"))));
    }

    // A diff shows bytes that are not UTF-8 as replacement characters, so a patch holding one would write them.
    [Fact]
    public void WhenAPatchHoldsAReplacementCharacter_ShouldRefuseItAsNotUtf8() =>
        Assert.Equal(WriteText.PartNotUtf8, Validate(new PatchRequest(Expected(), Path("one.txt"), "+caf�\n", PatchAction.Stage, Lines: true)));

    // Each path counts with the separator Git gets after it.
    [Fact]
    public void WhenTheSelectionIsExactlySixteenMegabytesOfPathData_ShouldAllowIt()
    {
        var name = new byte[GitLimits.SelectionPathBytes - 1];
        Array.Fill(name, (byte)'a');
        var path = new GitPath(name);

        Assert.Null(Validate(new StageRequest(Expected([new FileStatus(path, '.', 'M')]), [path])));
        Assert.Equal(WriteText.SelectionTooLarge, Validate(new StageRequest(Expected(), [new GitPath([.. name, (byte)'a'])])));
    }

    // An amend may only reword the last commit, so it needs no staged file; it needs a commit to replace.
    [Fact]
    public void WhenAnAmendHasNothingStaged_ShouldAllowIt() =>
        Assert.Null(Validate(new CommitRequest(Expected(), "subject", Amend: true)));

    [Fact]
    public void WhenAnAmendFindsNoCommitYet_ShouldSayThereIsNothingToAmend() =>
        Assert.Equal("There is no commit to amend yet.", Validate(new CommitRequest(Snapshots.Create(unborn: true), "subject", Amend: true)));

    [Fact]
    public void WhenAFileIsStaged_ShouldAllowTheCommit() =>
        Assert.Null(Validate(new CommitRequest(Expected([Snapshots.Staged("one.txt")]), "subject")));

    // Exactly one file is now the shape of the request itself; what is left to refuse is a file that is not in conflict.
    [Fact]
    public void WhenAToolIsOpenedForAFileOutsideTheStatus_ShouldAskForAConflictedFile()
    {
        var conflict = Snapshots.Conflict("one.txt");
        Assert.Equal("Select one conflicted file to open a tool.", Validate(new MergeToolRequest(Expected([conflict]), Path("other.txt"), Editor: true)));
    }

    [Fact]
    public void WhenAToolIsOpenedForAFileThatIsOnlyModified_ShouldAskForAConflictedFile() =>
        Assert.Equal("Select one conflicted file to open a tool.", Validate(new MergeToolRequest(Expected(), Path("one.txt"), Editor: false)));

    [Theory]
    [InlineData('D', 'U', false)]
    [InlineData('U', 'A', false)]
    [InlineData('U', 'U', true)]
    public void WhenTheMergeToolIsOpenedForAConflictThatIsNotAFileBothSidesChanged_ShouldAskToChooseASide(char index, char worktree, bool submodule)
    {
        var conflict = new FileStatus(Path("one.txt"), index, worktree, IsConflict: true, IsSubmodule: submodule);
        Assert.Equal("The merge tool works on a file both sides changed. Choose the side to keep instead.",
            Validate(new MergeToolRequest(Expected([conflict]), Path("one.txt"), Editor: false)));
        Assert.Null(Validate(new MergeToolRequest(Expected([conflict]), Path("one.txt"), Editor: true)));
    }

    // Git hands the tool the file by a name that is not its own UTF-8, which Trag cannot pass on as text.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WhenAToolIsOpenedForAConflictWhosePathIsNotUtf8_ShouldSayToUseGitTools(bool editor)
    {
        var path = new GitPath([(byte)'c', (byte)'a', (byte)'f', 0xE9]);
        var conflict = new FileStatus(path, 'U', 'U', IsConflict: true);
        Assert.Equal("This path cannot be named to Git as text. Use your Git tools for this file.",
            Validate(new MergeToolRequest(Expected([conflict]), path, editor)));
    }

    [Fact]
    public void WhenAToolIsOpenedForAConflictedFile_ShouldAllowIt()
    {
        var conflict = Snapshots.Conflict("one.txt");
        Assert.Null(Validate(new MergeToolRequest(Expected([conflict]), Path("one.txt"), Editor: true)));
    }

    [Fact]
    public void WhenNoFileIsSelectedForTheIndex_ShouldAskToSelectFiles() =>
        Assert.Equal("Select files before updating the index; the selection must fit within 16 MB of path data.",
            Validate(new StageRequest(Expected(), [])));

    [Fact]
    public void WhenTheSelectionIsOverSixteenMegabytesOfPathData_ShouldAskForASelectionThatFits()
    {
        var huge = Enumerable.Range(0, 17).Select(_ => new GitPath(new byte[1024 * 1024])).ToArray();
        Assert.Equal("Select files before updating the index; the selection must fit within 16 MB of path data.",
            Validate(new StageRequest(Expected(), huge)));
    }

    [Fact]
    public void WhenAFileOutsideTheStatusIsStaged_ShouldAskToRefreshAndSelectAgain()
    {
        const string refused = "The selection no longer matches the displayed status. Refresh and select the files again.";
        Assert.Equal(refused, Validate(new StageRequest(Expected(), [Path("other.txt")])));
    }

    [Fact]
    public void WhenAnUnstagedFileIsStaged_ShouldAllowIt() => Assert.Null(Validate(new StageRequest(Expected(), [Path("one.txt")])));

    // Staging asks for unstaged files, unstaging for staged ones: the same path is right for one and wrong for the other.
    [Fact]
    public void WhenAnUnstagedFileIsUnstaged_ShouldAskToRefreshAndSelectAgain()
    {
        const string refused = "The selection no longer matches the displayed status. Refresh and select the files again.";
        Assert.Equal(refused, Validate(new UnstageRequest(Expected(), [Path("one.txt")])));
    }

    [Fact]
    public void WhenAStagedFileIsUnstaged_ShouldAllowIt() =>
        Assert.Null(Validate(new UnstageRequest(Expected([Snapshots.Staged("one.txt")]), [Path("one.txt")])));

    [Fact]
    public void WhenAConflictedFileIsStagedAsAResolution_ShouldAllowIt()
    {
        var conflict = Snapshots.Conflict("one.txt");
        Assert.Null(Validate(new StageRequest(Expected([conflict]), [Path("one.txt")], Resolution: true)));
    }

    // Git puts a file back where a folder or a file now stands and deletes that, untracked files and all: a.txt became a
    // folder holding one, and the folder of dir/x.txt became a file. The discard and the restore are refused and name
    // them; an untracked file whose name only starts the same is no obstacle.
    [Fact]
    public void WhenUntrackedFilesStandWhereADiscardOrARestorePutsAFileBack_ShouldRefuseAndNameThem()
    {
        var expected = Expected([new(Path("a.txt"), '.', 'D'), Snapshots.Untracked("a.txt/inner.txt"), new(Path("dir/x.txt"), '.', 'D'),
            Snapshots.Untracked("dir"), Snapshots.Untracked("a.txt.bak"), Snapshots.Modified("b.txt")]);

        Assert.Equal(IndexOutcomeText.UntrackedInTheWay("a.txt/inner.txt, dir"), Validate(new DiscardRequest(expected, [Path("a.txt"), Path("dir/x.txt")])));
        Assert.Equal(FileVersionText.FilesInTheWay("a.txt/inner.txt"), Validate(new RestoreFileRequest(expected, Snapshots.Id('c'), Path("a.txt"))));
        Assert.Null(Validate(new DiscardRequest(expected, [Path("b.txt"), Path("a.txt.bak")])));
        Assert.Null(Validate(new RestoreFileRequest(expected, Snapshots.Id('c'), Path("b.txt"))));
    }

    // A path of an older commit can be a tracked folder now, and git restore deletes its changed files with the rest of it:
    // the restore is refused and names them, which are not untracked.
    [Fact]
    public void WhenChangedTrackedFilesStandWhereARestorePutsAFileBack_ShouldRefuseAndNameThem() =>
        Assert.Equal("Files with changes are where the restored file goes: a.txt/inner.txt. Git would delete them; move or delete them first.",
            Validate(new RestoreFileRequest(Expected([Snapshots.Modified("a.txt/inner.txt")]), Snapshots.Id('c'), Path("a.txt"))));

    // The status lists every untracked file of a folder on its own row, and the rule runs before Git with no way to cancel
    // it, so a discard of a large untracked folder is checked at once.
    [Fact]
    public async Task WhenADiscardChoosesFiftyThousandUntrackedFiles_ShouldCheckThemAtOnce()
    {
        var files = Enumerable.Range(0, 50_000).Select(index => Snapshots.Untracked("node_modules/package" + index + "/index.js")).ToArray();
        var check = Task.Run(() => Validate(new DiscardRequest(Expected(files), files.Select(file => file.Path).ToArray())));
        Assert.Same(check, await Task.WhenAny(check, Task.Delay(TimeSpan.FromSeconds(5))));
        Assert.Null(await check);
    }

    // git clean takes untracked names as text: one that is not its own UTF-8 is refused before anything is discarded, the
    // tracked files of the same selection included.
    [Fact]
    public void WhenADiscardHoldsAnUntrackedNameThatIsNotText_ShouldRefuseItBeforeAnythingIsDiscarded()
    {
        var odd = new GitPath([0x6f, 0x64, 0x64, 0xff]);
        var refusal = Validate(new DiscardRequest(Expected([Snapshots.Modified("one.txt"), new(odd, '?', '?')]), [Path("one.txt"), odd]));
        Assert.Equal("A selected file has a name Trag cannot pass to Git safely. Discard it in a terminal or your file manager; nothing was discarded.", refusal);
        // An untracked name that is text goes; left out of the selection the other is no obstacle; and a tracked file of
        // such a name is restored by its bytes.
        Assert.Null(Validate(new DiscardRequest(Expected([Snapshots.Untracked("new.txt")]), [Path("new.txt")])));
        Assert.Null(Validate(new DiscardRequest(Expected([Snapshots.Modified("one.txt"), new(odd, '?', '?')]), [Path("one.txt")])));
        Assert.Null(Validate(new DiscardRequest(Expected([new(odd, '.', 'M')]), [odd])));
    }

    // git clean takes the name of a file git add -N only intended to add once git rm --cached has let it go, and git
    // submodule update a submodule's name: both on the command line, as text, so one that is not its own UTF-8 is refused
    // as an untracked one is.
    [Fact]
    public void WhenADiscardHoldsAnIntentToAddOrSubmoduleNameThatIsNotText_ShouldRefuseItBeforeAnythingIsDiscarded()
    {
        var odd = new GitPath([0x6f, 0x64, 0x64, 0xff]);
        Assert.Equal(IndexOutcomeText.NameUnsafe, Validate(new DiscardRequest(Expected([new(odd, '.', 'A')]), [odd])));
        Assert.Equal(IndexOutcomeText.NameUnsafe, Validate(new DiscardRequest(Expected([new(odd, '.', 'M', IsSubmodule: true, IsSubmoduleMoved: true)]), [odd])));
    }

    // With every untracked file listed, the one untracked entry Git names as a folder is a folder holding a repository of
    // its own. git clean leaves it alone without a word, so the discard is refused whole before anything is discarded.
    [Fact]
    public void WhenADiscardHoldsAFolderThatIsARepositoryOfItsOwn_ShouldRefuseItBeforeAnythingIsDiscarded()
    {
        var files = Expected([Snapshots.Modified("one.txt"), Snapshots.Untracked("inner/"), Snapshots.Untracked("notes.txt")]);
        const string refused = "Folders that are Git repositories of their own: inner/. Trag does not delete a repository; nothing was discarded.";

        Assert.Equal(refused, Validate(new DiscardRequest(files, [Path("inner/")])));
        Assert.Equal(refused, Validate(new DiscardRequest(files, [Path("one.txt"), Path("inner/")])));
        Assert.Null(Validate(new DiscardRequest(files, [Path("one.txt"), Path("notes.txt")])));
    }

    [Fact]
    public void WhenAConflictedFileIsDiscarded_ShouldRefuseItAsNotMatchingTheStatus()
    {
        var conflict = Snapshots.Conflict("one.txt");
        Assert.Equal("The selection no longer matches the displayed status. Refresh and select the files again.",
            Validate(new DiscardRequest(Expected([conflict]), [Path("one.txt")])));
    }

    [Fact]
    public void WhenASelectedPathIsEmpty_ShouldRefuseTheSelection()
    {
        const string refused = "The selection no longer matches the displayed status. Refresh and select the files again.";
        Assert.Equal(refused, Validate(new StageRequest(Expected(), [new GitPath([])])));
    }

    [Fact]
    public void WhenASelectedPathHasANul_ShouldRefuseTheSelection()
    {
        const string refused = "The selection no longer matches the displayed status. Refresh and select the files again.";
        Assert.Equal(refused, Validate(new StageRequest(Expected(), [new GitPath("one\0two"u8.ToArray())])));
    }

    // What only Git could check is the writer's; a commit, a tag, a start point, an upstream or a stash entry no Git could
    // know, and a stash message Git could not keep, are refused before Git starts.
    [Fact]
    public void WhenARequestNamesWhatNoGitCouldKnow_ShouldRefuseItBeforeGitStarts()
    {
        var expected = Expected();
        Assert.Equal(HistoryText.CommitGone, Validate(new RevertRequest(expected, "not-an-id")));
        Assert.Equal(HistoryText.CommitGone, Validate(new CherryPickRequest(expected, "not-an-id")));
        Assert.Equal(HistoryText.CommitGone, Validate(new TagRequest(expected, new("v1.0", "not-an-id"))));
        Assert.Equal(TagOutcomeText.NameInvalid, Validate(new TagRequest(expected, new("v 1.0", Snapshots.HeadId))));
        Assert.Equal(BranchOutcomeText.CommitMissing, Validate(new BranchRequest(expected, new("main"), BranchAction.Checkout)));
        Assert.Equal(BranchOutcomeText.StartInvalid, Validate(new BranchRequest(expected, new("topic", "bad start"), BranchAction.Create)));
        Assert.Equal(BranchOutcomeText.TrackInvalid, Validate(new BranchRequest(expected, new("topic", null, true, "refs/heads/main"), BranchAction.Create)));
        Assert.Equal(StashOutcomeText.Missing, Validate(new StashRequest(expected, new(Id: "abc"), StashAction.Apply)));
        Assert.Equal(StashOutcomeText.MessageInvalid, Validate(new StashRequest(expected, new("-keep"), StashAction.Push)));
        Assert.Null(Validate(new BranchRequest(expected, new("topic", null, true, "refs/remotes/origin/topic"), BranchAction.Create)));
        Assert.Null(Validate(new BranchRequest(expected, new(Snapshots.HeadId[..7]), BranchAction.Checkout)));
    }

    // The last arm of the validator: refused rather than quietly allowed.
    [Fact]
    public void WhenTheRequestTypeIsUnknown_ShouldRefuseIt() =>
        Assert.Equal("Unknown write operation.", Validate(new UnknownRequest(Expected())));

    private sealed record UnknownRequest(RepositorySnapshot Expected) : GitWriteRequest(Expected);
}
