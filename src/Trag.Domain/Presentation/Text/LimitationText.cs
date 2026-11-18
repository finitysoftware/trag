namespace Trag.Domain.Presentation.Text;

// Why Trag shows a repository but will not do everything in it: what the scan of the repository found.
public static class LimitationText
{
    public const string RecoveryIndex = "A Trag recovery index needs review in your Git tools. Compare it with HEAD and the working files before moving it out of the Git directory. Trag will not delete it automatically.";
    public const string InterruptedWrite = "A Trag recovery index (trag-write-*.index) was left beside index.lock in the Git directory by a write Trag did not finish. Git may have finished the write, so the recovery index is the one Git made: once no Git program is running, put it in place of the index file there, then delete index.lock.";
    public const string LinkedWorktree = "Linked worktree: repository writes are not supported yet.";
    public const string SparseCheckout = "Sparse checkout: repository writes are not supported yet.";
    public const string PartialClone = "Partial clone: repository writes are not supported yet.";
    public const string LfsNotInstalled = "Git LFS files without Git LFS set up for Git: repository writes are not supported until Git LFS is installed and git lfs install has run.";
}
