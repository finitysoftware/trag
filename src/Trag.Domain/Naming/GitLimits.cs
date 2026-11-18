namespace Trag.Domain.Naming;

// The sizes Trag accepts before it asks Git. They bound what a dialog offers and what a reader or writer passes on, so
// the same number is never written in two places.
public static class GitLimits
{
    // A branch or remote name; Git allows longer refs, the application does not.
    public const int NameLength = 255;
    // How long a transfer may go without a word from Git, its progress or its output, before it is stopped.
    public const int TransferQuietMinutes = 2;
    // A start point, a tracking ref or a refspec.
    public const int RefLength = 1024;
    public const int QueryLength = 1024;
    public const int StashMessageLength = 1024;
    // A repository name for a hosting site, and a remote address.
    public const int PublishNameLength = 100;
    public const int AddressLength = 2048;
    public const int CommitMessageBytes = 64 * 1024;
    // The paths of one index update, as Git reads them from its input.
    public const int SelectionPathBytes = 16 * 1024 * 1024;
    // Branch fetch mappings of one remote.
    public const int FetchMappings = 32;
    // Characters of a commit message the details pane shows; search always uses the whole message.
    public const int CommitMessageDisplay = 16_384;
    // Branches read for the picker, and upstream branches shown for sync.
    public const int BranchCount = 5000;
    // The oldest Git Trag works with; an older one is refused before anything is read from the repository.
    public static readonly Version MinimumGit = new(2, 43, 0);
}
