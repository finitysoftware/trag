using Trag.Domain.Status;

namespace Trag.Infrastructure.Git.State;

// Which Git operation is in progress, read from the marker files Git itself leaves in the Git directory and the refs it
// keeps beside HEAD. The order matters: a rebase that is applying a mailbox says so, and a pick head or a merge marker
// during a sequencer run, as a cherry-pick or revert of a range stopped part way leaves, is not one pick or a plain merge.
internal static class RepositoryOperationDetector
{
    public static RepositoryOperation Detect(string gitDirectory, RootRefs roots)
    {
        bool FileExists(string name) => File.Exists(Path.Combine(gitDirectory, name));
        bool DirectoryExists(string name) => Directory.Exists(Path.Combine(gitDirectory, name));
        if (DirectoryExists("rebase-merge")) return RepositoryOperation.Rebase;
        if (DirectoryExists("rebase-apply")) return FileExists("rebase-apply/applying") ? RepositoryOperation.ApplyMailbox : RepositoryOperation.Rebase;
        if (DirectoryExists("sequencer")) return RepositoryOperation.Sequencer;
        if (roots["CHERRY_PICK_HEAD"] is not null) return RepositoryOperation.CherryPick;
        if (roots["REVERT_HEAD"] is not null) return RepositoryOperation.Revert;
        if (FileExists("MERGE_HEAD")) return RepositoryOperation.Merge;
        if (FileExists("BISECT_START")) return RepositoryOperation.Bisect;
        return RepositoryOperation.None;
    }
}
