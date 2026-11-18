using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Write;

// What a failed stash command means to the user, read from Git's own output.
internal static class StashFailureText
{
    private static readonly (string[] Phrases, string Message)[] Table =
    [
        (["untracked working tree files would be overwritten"], StashOutcomeText.UntrackedInTheWay),
        (["would be overwritten", "commit your changes or stash them"],
            StashOutcomeText.LocalChangesInTheWay),
        (["already exists"], StashOutcomeText.FilesInTheWay),
        // Git 2.45.2 and 2.50.0 refuse a stash on a locked index saying only that they could not write it.
        (["index.lock", "could not write index"], WriteText.IndexLocked),
        // A file another program holds is one Git cannot read; it then says it cannot index it too, but deletes nothing.
        (["Permission denied"], StashOutcomeText.Failed),
        (["unable to index file"], StashOutcomeText.UntrackedNotIndexable)
    ];

    public static string Explain(GitResult result) =>
        GitFailureText.Classify(GitOutputText.Details(result), Table, StashOutcomeText.Failed);
}
