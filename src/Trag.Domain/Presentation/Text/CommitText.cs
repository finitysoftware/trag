using Trag.Domain.Diff;
using Trag.Domain.History;
using Trag.Domain.Naming;
using Trag.Domain.Status;
using Trag.Domain.Writes;

namespace Trag.Domain.Presentation.Text;

// What the commit details pane says (the identity line, the message within the display limit, what the file list is
// compared against, the file rows, why there is no commit to show) and what the commit composer says (Git's identities
// as the snapshot carries them, the result of a write, the button captions).
public static class CommitText
{
    public const string SelectCommit = "Select a commit to see its details.";
    public const string Loading = "Loading commit…";
    public const string Changed = "Repository changed. Select a commit from current history.";
    public const string NotRead = "This commit could not be read. Refresh and select it again.";
    public const string LoadCanceled = "Commit loading canceled.";
    public const string ResultNotDisplayed = "The operation outcome could not be displayed. Refresh and review HEAD and staged content before trying again.";

    public static string Identity(HistoryCommit commit) => GitIdentity.Format(commit.Author, commit.Email) + " · " + commit.Date;

    // What Git's check of the commit's signature found; nothing for a commit it found no signature on.
    public static string Signature(CommitSignature signature) => signature.Check switch
    {
        SignatureCheck.Good => "Signed by " + signature.Signer + ".",
        SignatureCheck.Untrusted => "Signed by " + signature.Signer + ", whose key this computer does not trust.",
        SignatureCheck.Expired => "Signed by " + signature.Signer + "; the signature has expired.",
        SignatureCheck.ExpiredKey => "Signed by " + signature.Signer + " with a key that has expired.",
        SignatureCheck.Revoked => "Signed by " + signature.Signer + " with a key that was revoked.",
        SignatureCheck.Bad => "Git reports a bad signature: the commit does not match what was signed.",
        SignatureCheck.CannotCheck => "Signed, but this computer cannot check the signature: it does not have the key.",
        SignatureCheck.CheckerMissing => "Signed, but Git could not start the program that checks signatures. Check gpg.program, or gpg.ssh.program for an SSH key.",
        SignatureCheck.NoAllowedSigners => "Signed with an SSH key, but Git has no list of allowed signers to check it against (gpg.ssh.allowedSignersFile).",
        SignatureCheck.NotFinished => "Trag stopped waiting for Git to check the signature after 30 seconds. Check gpg.program, or gpg.ssh.program for an SSH key.",
        _ => ""
    };

    // The co-authors the message credits, by name; nothing without one.
    public static string CoAuthors(IReadOnlyList<GitIdentity> people) => people.Count == 0 ? "" : "with " + string.Join(", ", people.Select(person => person.Name));

    public static string Message(string message) => message.Length <= GitLimits.CommitMessageDisplay ? message
        : message[..GitLimits.CommitMessageDisplay] + "\n… Message display limited to " + Counted.Grouped(GitLimits.CommitMessageDisplay) + " characters; search uses the complete message.";

    // A commit with no parent is compared with an empty tree, and so is the oldest commit of a shallow clone, whose parent
    // is not there; a merge names only its first parent, because that is the diff Git gives without being asked.
    public static string Comparison(CommitDetails details) => details.IsShallowBoundary
        ? "Oldest commit of this shallow clone · its parent is not here, so it is compared with an empty tree"
        : details.Parents.Count == 0 ? "Root commit · compared with an empty tree"
        : details.Parents.Count == 1 ? "Compared with parent " + details.Parents[0]
        : "Merge commit · diff against first parent " + details.Parents[0];

    public static string Files(CommitDetails details) =>
        details.FileNotice.Length != 0 ? details.FileNotice
        : details.Files.Count == 0 ? "This commit has no file changes against this parent."
        : Counted.Of(details.Files.Count, "changed file");

    public static string FileDescription(FileDiffMetadata file) =>
        file.OriginalPath is null ? file.Path.DisplayEscaped : file.OriginalPath.DisplayEscaped + " → " + file.Path.DisplayEscaped;

    // The commit button: a new commit of what is staged, or, with Amend checked, a replacement of the last one.
    public static string Action(bool amend) => amend ? "Amend commit" : "Commit staged";
    public static string Tip(bool amend) => amend ? "Replace the last commit with this message and whatever is staged (Ctrl+Enter)" : "Commit staged changes (Ctrl+Enter)";
    public const string AmendMessageNotRead = "The last commit's message could not be read, so the amend did not start. Your message is as you left it.";
    public static string AmendPushed(string upstream) => "This commit is already on " + upstream + ". After amending, Push will have to replace it there.";

    // Without a repository there is nothing to show; the committer is named only when it differs from the author.
    public static string Author(RepositorySnapshot? snapshot) => snapshot switch
    {
        null => "Git identity is not available.",
        { Author: null } => "Set Git user.name and user.email, then refresh to enable commit.",
        { Author: { } author } => "Author: " + author.Display
    };

    public static string Committer(RepositorySnapshot? snapshot) => snapshot switch
    {
        null => "",
        { Committer: null } => "Committer identity is missing.",
        { Committer: { } committer, Author: var author } when committer != author => "Committer: " + committer.Display,
        _ => ""
    };

    // Whether Git signs the commit about to be made, and with what; nothing when it does not.
    public static string Signing(CommitSigning signing) => signing switch
    {
        CommitSigning.OpenPgp => "Commits are signed with your GPG key.",
        CommitSigning.Ssh => "Commits are signed with your SSH key.",
        CommitSigning.X509 => "Commits are signed with your X.509 certificate.",
        _ => ""
    };

    public static string StageAction(int selected) => selected > 1 ? "Stage " + Counted.Of(selected, "file") : "Stage selected";
    public static string UnstageAction(int selected) => selected > 1 ? "Unstage " + Counted.Of(selected, "file") : "Unstage selected";

    // The commit's file list stops at the limit and says so.
    public static readonly string FileLimitNotice = "Showing up to " + Counted.Grouped(HistoryLimits.FileLimit) + " changed files. This commit may contain more files.";
}
