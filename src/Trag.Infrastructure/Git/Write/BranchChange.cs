using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;

namespace Trag.Infrastructure.Git.Write;

internal sealed record BranchChange(BranchAction Action, string Name, string? Start, string? Track, bool Switch)
{
    public bool IsSwitching => Action == BranchAction.Switch || Switch;
    public string LocalRef => "refs/heads/" + Name;

    public static BranchChange From(BranchRequest request) => new(request.Action, request.Branch.Name.Trim(),
        request.Branch.StartPoint?.Trim(), request.Branch.Track?.Trim(), request.Branch.Switch);

    public GitWriteResult? CheckExisting(bool exists, string? currentBranch)
    {
        if (Action == BranchAction.Create && exists)
            return GitWriteResult.Rejected(BranchOutcomeText.NameTaken(Name));
        if (Action != BranchAction.Switch) return null;
        if (!exists) return GitWriteResult.Rejected(BranchOutcomeText.Gone);
        if (Name == currentBranch) return new(GitWriteOutcome.Succeeded, BranchText.AlreadyOn(Name));
        return null;
    }

    // A switch refuses to replace an ignored file, as it refuses an untracked one: Git's default overwrites it, and a .env
    // or a local config is no less the user's own.
    public string[] Arguments()
    {
        if (Action == BranchAction.Switch) return ["switch", "--no-guess", "--no-overwrite-ignore", Name];
        if (!Switch)
        {
            if (Track is not null) return ["branch", "--track", Name, Track];
            return ["branch", Name, Start ?? "HEAD"];
        }
        if (Track is not null) return ["switch", "--no-guess", "--no-overwrite-ignore", "-c", Name, "--track", Track];
        return ["switch", "--no-guess", "--no-overwrite-ignore", "-c", Name, Start ?? "HEAD"];
    }

    public string SwitchedMessage() => Action == BranchAction.Create
        ? BranchOutcomeText.CreatedAndOn(Name)
        : BranchOutcomeText.Switched(Name);

    public GitWriteResult? CompletedResult(RepositorySnapshot before, string? head, string? branch, bool exists)
    {
        if (IsSwitching) return branch == Name ? new(GitWriteOutcome.Succeeded, SwitchedMessage()) : null;
        if (!exists || head != before.Head || branch != before.Branch) return null;
        return new(GitWriteOutcome.Succeeded, BranchOutcomeText.Created(Name, before.Branch));
    }

    public bool IsUnchanged(RepositorySnapshot before, string? head, string? branch, bool exists) =>
        head == before.Head && branch == before.Branch && (Action == BranchAction.Switch || !exists);
}
