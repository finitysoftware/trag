using Trag.Application.Live;
using Trag.Application.Sync;
using Trag.Domain.Branches;
using Trag.Domain.Merge;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Sync;
using Trag.Domain.Writes;

namespace Trag.Application.Merge;

// A merge the dialog confirmed, as the writes it takes, each verified on its own. A remote branch's Git LFS content comes
// down first, with its remote's credentials, so the merge checks the files out from what is here. Into the current branch
// it is one merge.
// Into another local branch Trag switches there first and back afterwards, unless the user stays or Git leaves a merge to
// resolve there. A push after merging runs while the target is still checked out, because Push sends the current branch.
// The answer is the merge's own result, told as the whole story; null when the merge itself never ran.
public sealed class MergeFlow(RepositoryWriter writer, RemotesModel remotes, StatusBarState statusBar)
{
    public async Task<GitWriteResult?> RunAsync(MergeChoice choice, MergePreview preview)
    {
        if (choice is not { From: { } from, Into: { } into } || !preview.IsRead) return null;
        if (from is { IsRemote: true, Remote: { } remote })
        {
            var downloaded = await writer.RunAsync(expected => new LfsDownloadRequest(expected, remote, preview.FromHead));
            if (downloaded?.Outcome != GitWriteOutcome.Succeeded) return downloaded;
        }
        if (choice.SwitchesBranch)
        {
            var switched = await SwitchAsync(into.Name);
            if (switched?.Outcome != GitWriteOutcome.Succeeded) return switched;
        }
        // A message the user emptied and the dialog then hid for a fast-forward goes as Trag's own: Git may still have to
        // write a merge commit, and the writer refuses a merge without a message.
        var message = CommitRequest.IsValidMessage(choice.Message) ? choice.Message : MergeMessages.Default(from, into);
        var merged = await writer.RunAsync(expected => new MergeRequest(expected, from.Name, preview.FromHead, choice.NoFastForward, message));
        if (merged is null) return null;
        List<string> story = [merged.Message];
        if (choice.PushAfter && merged.Outcome == GitWriteOutcome.Succeeded) story.Add(await PushAsync(into.Name));
        if (choice.SwitchesBranch && choice.Current is { } start)
        {
            // Staying is for a merge that happened; a refused one goes back to where the user started.
            if (merged.Outcome == GitWriteOutcome.NeedsResolution) story.Add(MergeText.StaysForConflicts(into.Name));
            else if (!choice.StayOnTarget || merged.Outcome != GitWriteOutcome.Succeeded) story.Add(await SwitchBackAsync(start));
        }
        statusBar.Status = string.Join(" ", story);
        return merged with { Message = statusBar.Status };
    }

    private Task<GitWriteResult?> SwitchAsync(string branch) =>
        writer.RunAsync(expected => new BranchRequest(expected, new BranchOperation(branch), BranchAction.Switch));

    private async Task<string> SwitchBackAsync(string branch)
    {
        var back = await SwitchAsync(branch);
        return back is { Outcome: GitWriteOutcome.Succeeded } ? MergeText.SwitchedBack(branch) : MergeText.NotSwitchedBack(branch, back?.Message);
    }

    // Push sends the checked-out branch to the upstream it tracks; a branch without one is left for the sidebar to publish,
    // and an upstream that cannot be read is not pushed to.
    private async Task<string> PushAsync(string branch)
    {
        RepositorySyncState? tracked;
        try { tracked = await remotes.ReadTrackedAsync(); }
        catch (Exception) { return MergeText.NotPushed(branch, Failures.UpstreamNotRead); }
        if (tracked is null) return MergeText.NotPushed(branch, null);
        var pushed = await remotes.PushToAsync(tracked);
        return pushed is { Outcome: GitWriteOutcome.Succeeded } ? MergeText.Pushed(branch) : MergeText.NotPushed(branch, pushed?.Message ?? Failures.PushNotStarted);
    }
}
