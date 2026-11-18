using Trag.Domain.Status;

namespace Trag.Domain.Presentation.Text;

// What a commit write says about how it ended: created, canceled, rejected by Git, or created but not confirmed.
public static class CommitOutcomeText
{
    public const string Created = "Commit created from staged content.";
    public const string MergeCreated = "Merge commit created and both parents verified.";
    public const string Amended = "Last commit amended.";
    public const string CreatedAfterCancel = "Commit created and verified after cancellation.";
    public const string Canceled = "Commit canceled; verification found no new commit. Your message and original index are kept.";
    public const string Rejected = "Git rejected the commit. Check the hook or signing output below. Your message and original index are kept.";
    public const string StoppedEarly = "Git stopped before returning a complete result.";
    public const string StoppedUnverified = "Git stopped, and the commit outcome could not be verified. Refresh and review HEAD before trying again.";
    public const string Uncertain = "The commit outcome is uncertain. Review HEAD and staged content before trying again.";
    public const string ChangedBeforeSync = "A commit was created, but the repository changed before its index could be synchronized. Review it before continuing.";
    public const string NotSynchronized = "The commit was created, but its index could not be synchronized. Review it before continuing.";
    public const string ShallowAmend = "The commit before the last one is not in this shallow clone, so amending would write the last commit anew without its older history. Fetch the full history in Git first.";

    public static string NotSigned(CommitSigning signing) => NotSigned(signing, "Your message and staged changes are kept.");

    // A commit Git could not sign, with what to check for the kind of key it signs with, then what became of the write.
    public static string NotSigned(CommitSigning signing, string then) => NotSigned("commit", signing, then);

    // The same for any object Git signs: a commit or a tag.
    public static string NotSigned(string what, CommitSigning signing, string then) => "Git could not sign the " + what + " with your " + signing switch
    {
        CommitSigning.Ssh => "SSH key. A key with a passphrase has to be in an ssh-agent the signing program reaches: for Windows' own agent, set gpg.ssh.program to C:/Windows/System32/OpenSSH/ssh-keygen.exe.",
        CommitSigning.X509 => "X.509 certificate. Check that gpgsm can sign outside Trag.",
        _ => "GPG key. Check that gpg and its agent can sign outside Trag."
    } + " " + then;
}
