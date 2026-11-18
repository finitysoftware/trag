using Trag.Domain.Diff;
using Trag.Domain.Merge;
using Trag.Domain.Rebase;
using Trag.Domain.Naming;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Tags;

namespace Trag.Domain.Writes;

// Everything that can be refused before Git is started, each refusal in WriteText's words (a reset's in ResetText's, a
// restored or reverted file's in FileVersionText's, a remote's in RemoteText's, a tag's in TagOutcomeText's). Each
// request answers for itself: a commit, a tag, a start point, an upstream or a stash entry no Git could know is refused
// here, and only what Git has to look up is the writer's. A sync, a publish, a remote
// branch deletion, a remote tag, an LFS download and a submodule update are the synchronizer's and never reach it. The
// last arm is what a request type nobody taught this validator about gets.
public static class GitWriteRequestValidator
{
    public static string? Validate(GitWriteRequest request) => request switch
    {
        CommitRequest commit => Commit(commit),
        MergeRequest merge => Merge(merge),
        RebaseRequest rebase => GitObjectId.IsValid(rebase.OntoHead) ? null : WriteText.RebaseOntoMissing,
        RebaseContinueRequest next => CommitRequest.IsValidMessage(next.Message) ? null : WriteText.CommitMessageInvalid,
        MergeToolRequest tool => Tool(tool),
        ConflictSideRequest side => side.Side == ConflictChoice.Both ? WriteText.BothSidesChosen : Conflicted(side.Expected, side.Path),
        ConflictBlockRequest block => block.Block < 0 || block.ContentHash.Length == 0 ? WriteText.ConflictBlockMissing : Conflicted(block.Expected, block.Path),
        ReopenConflictRequest reopen => reopen.Expected.Conflicts.Any(file => file.Path.IsSame(reopen.Path)) ? WriteText.AlreadyInConflict : null,
        StageRequest stage => Selection(stage.Expected, stage.Paths, file => file.IsUnstaged),
        UnstageRequest unstage => Selection(unstage.Expected, unstage.Paths, file => file.IsStaged),
        DiscardRequest discard => Selection(discard.Expected, discard.Paths, file => file.IsWorkingTreeChange) ?? NamesAsText(discard) ?? Repositories(discard)
            ?? InTheWay(discard.Expected, discard.Paths, IndexOutcomeText.UntrackedInTheWay),
        ResetRequest reset => GitObjectId.IsValid(reset.Commit) ? null : ResetText.CommitGone,
        RestoreFileRequest restore => !restore.Path.IsDisplayable ? FileVersionText.NameNotText : GitObjectId.IsValid(restore.Commit)
            ? InTheWay(restore.Expected, [restore.Path], FileVersionText.FilesInTheWay) : FileVersionText.CommitGone,
        RevertFileRequest revert => !revert.Path.IsDisplayable || revert.OriginalPath is { IsDisplayable: false } ? FileVersionText.NameNotText
            : GitObjectId.IsValid(revert.Commit) ? null : FileVersionText.RevertCommitGone,
        RewriteRequest rewrite => rewrite.Steps.Count == 0 || rewrite.Base is { } start && !GitObjectId.IsValid(start) || rewrite.Steps.Any(step => !GitObjectId.IsValid(step.Id))
            ? RewriteText.Unreadable : RewritePlan.Refusal(rewrite.Steps),
        // A diff shows bytes that are not UTF-8 as replacement characters, and a patch made from it would write them.
        PatchRequest { Patch: var text } when text.Contains('\uFFFD') => WriteText.PartNotUtf8,
        PatchRequest patch => patch.Expected.Files.Any(file => file.Path.IsSame(patch.Path) && HunkPatch.Fits(file, patch.Action == PatchAction.Unstage))
            ? null : WriteText.HunkGone,
        RemoteAddRequest add => Refusal(RemoteEdits.AddError(add.Expected, add.Name, add.Url)),
        RemoteEditRequest change => Refusal(RemoteEdits.EditError(change.Expected, change.Name, change.NewName, change.Url)),
        RemoteRemoveRequest remove => Refusal(RemoteEdits.RemoveError(remove.Expected, remove.Name)),
        IgnoreRequest ignore => ignore.Pattern.Length == 0 || ignore.Pattern.Contains('\n') || ignore.Pattern.Contains('\r') ? IgnoreText.LineInvalid : null,
        TagRequest tag => Tag(tag),
        RevertRequest revert => Known(revert.Commit),
        CherryPickRequest pick => Known(pick.Commit),
        BranchRequest branch => Branch(branch),
        StashRequest stash => Stash(stash),
        TagDeleteRequest or BranchRenameRequest or MergeAbortRequest or RebaseSkipRequest or RebaseAbortRequest or PickContinueRequest or PickAbortRequest => null,
        _ => WriteText.UnknownWrite
    };

    private static string? Refusal(string error) => error.Length == 0 ? null : error;

    private static string? Known(string commit) => GitObjectId.IsValid(commit) ? null : HistoryText.CommitGone;

    // A tag's name has to be one Git could take; whether Git does is the writer's question. Where tags are signed, each one
    // needs its message.
    private static string? Tag(TagRequest request) => TagName(request.Tag.Name) ?? Known(request.Tag.Commit)
        ?? (TagOperation.LacksMessage(request.Expected.SignsTags, request.Tag.Message) ? TagOutcomeText.NeedsMessage : null);
    private static string? TagName(string name) => RefNames.HasSafeCharacters(name.Trim(), GitLimits.NameLength) ? null : TagOutcomeText.NameInvalid;

    // A commit to look at is named by its id; a new branch starts at a ref Git could name and tracks a remote-tracking one.
    private static string? Branch(BranchRequest request) => request.Branch switch
    {
        { Name: var commit } when request.Action == BranchAction.Checkout => GitObjectId.IsAbbreviation(commit.Trim()) ? null : BranchOutcomeText.CommitMissing,
        { StartPoint: { } start } when !RefNames.HasSafeCharacters(start.Trim(), GitLimits.RefLength) => BranchOutcomeText.StartInvalid,
        { Track: { } track } when !RefNames.HasSafeCharacters(track.Trim(), GitLimits.RefLength)
            || !RefNames.IsRemoteTracking(track.Trim()) => BranchOutcomeText.TrackInvalid,
        _ => null
    };

    // A new stash keeps a message Git can take; any other stash write names an entry by its id.
    private static string? Stash(StashRequest request) => request.Action == StashAction.Push
        ? StashMessages.IsValid(request.Stash.Message?.Trim() ?? "") ? null : StashOutcomeText.MessageInvalid
        : request.Stash.Id?.Trim() is { } id && GitObjectId.IsValid(id) ? null : StashOutcomeText.Missing;

    private static string? Commit(CommitRequest request)
    {
        if (CoAuthors.LacksSubject(request.Message)) return WriteText.SubjectMissing;
        if (!CommitRequest.IsValidMessage(request.Message)) return WriteText.CommitMessageInvalid;
        if (request.Amend) return request.Expected.IsUnborn ? WriteText.NothingToAmend : null;
        if (!request.FinishesMerge && !request.Expected.Files.Any(file => file.IsStaged)) return WriteText.NothingStaged;
        return null;
    }

    // The message is checked even for a fast-forward, which does not use it: what Git would do is only known afterwards.
    private static string? Merge(MergeRequest request)
    {
        if (!GitObjectId.IsValid(request.FromHead)) return WriteText.MergeBranchMissing;
        if (!CommitRequest.IsValidMessage(request.Message)) return WriteText.MergeMessageInvalid;
        return null;
    }

    private static string? Conflicted(RepositorySnapshot expected, GitPath path) =>
        expected.Conflicts.Any(file => file.Path.IsSame(path)) ? null : WriteText.ConflictedFileMissing;

    private static string? Tool(MergeToolRequest request) =>
        request.Expected.Files.FirstOrDefault(file => file.IsConflict && file.Path.IsSame(request.Path)) is not { } file ? WriteText.ToolFileMissing
            : !request.Path.IsDisplayable ? ConflictOutcomeText.PathNotNameable
            : !request.Editor && !new MergeConflict(file, null).TakesMergeTool ? WriteText.ToolNeedsBothSides : null;

    // git clean takes untracked names on its command line as text, a name git add -N only intended to add among them, and
    // git submodule update a submodule's, so a name that is not its own UTF-8 cannot go there.
    private static string? NamesAsText(DiscardRequest discard) =>
        discard.Expected.Files.Any(file => (file.IsUntracked || file.IsIntentToAdd || file.IsSubmodule) && !file.Path.IsDisplayable
            && discard.Paths.Any(path => path.IsSame(file.Path))) ? IndexOutcomeText.NameUnsafe : null;

    // git clean leaves a folder holding a repository of its own alone, and says nothing about it; a discard never deletes
    // another repository's history, so the selection is refused whole, as one with a submodule's own changes is.
    private static string? Repositories(DiscardRequest discard) =>
        discard.Expected.Files.Where(file => file.IsRepositoryOfItsOwn && discard.Paths.Any(path => path.IsSame(file.Path)))
            .Select(file => file.Path.DisplayEscaped).ToArray() is { Length: > 0 } kept
            ? IndexOutcomeText.RepositoriesKept(string.Join(", ", kept)) : null;

    // Git puts a file back where a folder or a file now stands and deletes that, untracked files and all, without a word.
    // In a discard only an untracked file can be there, since the index never holds a path both as a file and as a folder;
    // a restore from an older commit can also find a tracked folder there, changed files and all. The status lists every
    // untracked file on its own row, so the rule looks each one up rather than comparing it with every chosen path.
    private static string? InTheWay(RepositorySnapshot expected, IReadOnlyList<GitPath> paths, Func<string, string> refusal)
    {
        var chosen = paths.Select(path => path.Key).ToHashSet(StringComparer.Ordinal);
        var folders = paths.SelectMany(Folders).ToHashSet(StringComparer.Ordinal);
        return expected.Files.Where(file => folders.Contains(file.Path.Key) || Folders(file.Path).Any(chosen.Contains))
            .Select(file => file.Path.DisplayEscaped).ToArray() is { Length: > 0 } held ? refusal(string.Join(", ", held)) : null;
    }

    private static IEnumerable<string> Folders(GitPath path)
    {
        for (var index = 0; index < path.Bytes.Length; index++)
            if (path.Bytes[index] == (byte)'/')
                yield return Convert.ToHexString(path.Bytes.AsSpan(0, index));
    }

    // The selection has to be small enough to hand to Git, and every path in it has to still be one the displayed
    // status offers for this operation - a rename offers both of its names. An empty or NUL-bearing path is never
    // one of those, so it needs no rule of its own.
    private static string? Selection(RepositorySnapshot expected, IReadOnlyList<GitPath> paths, Func<FileStatus, bool> offered)
    {
        if (paths.Count == 0 || paths.Sum(path => (long)path.Bytes.Length + 1) > GitLimits.SelectionPathBytes)
            return WriteText.SelectionTooLarge;
        var allowed = expected.Files.Where(offered)
            .SelectMany(file => file.Paths).Select(path => path.Key).ToHashSet(StringComparer.Ordinal);
        return paths.All(path => allowed.Contains(path.Key))
            ? null : WriteText.SelectionStale;
    }
}
