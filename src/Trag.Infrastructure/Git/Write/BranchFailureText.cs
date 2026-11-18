using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Write;

// What a failed branch command means to the user, read from Git's own output.
internal static class BranchFailureText
{
    private static readonly (string[] Phrases, string Message)[] Table =
    [
        (["untracked working tree files would be overwritten"], BranchOutcomeText.UntrackedInTheWay),
        (["would lose untracked files"], BranchOutcomeText.UntrackedInTheWay),
        (["would be overwritten", "commit your changes or stash them"],
            BranchOutcomeText.LocalChangesInTheWay),
        (["used by worktree"], BranchOutcomeText.TargetInAnotherWorktree),
        (["already exists"], BranchOutcomeText.NameExists),
        (["index.lock"], WriteText.IndexLocked)
    ];

    public static string Explain(GitResult result) =>
        GitFailureText.Classify(GitOutputText.Details(result), Table, BranchOutcomeText.Failed);

    // HEAD is where it was: when Git ran to the end that is Git's refusal, in its own words; otherwise the command was
    // interrupted before it changed anything, and the caller says so for its operation.
    public static GitWriteResult Unchanged(GitResult? command, Exception? failure, string interrupted) =>
        failure is null && command is not null
            ? GitWriteResult.Rejected(Explain(command), GitOutputText.Details(command))
            : new(GitWriteOutcome.Cancelled, interrupted);
}
