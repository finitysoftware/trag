using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// The key a write signs with: a commit, a merge, a rebase, a commit edit, a revert or a cherry-pick signs its commits as
// commit.gpgSign says, and a tag as tag.gpgSign says; anything else signs nothing.
public static class SigningWrites
{
    public static CommitSigning Key(GitWriteRequest request) => request switch
    {
        TagRequest tag => tag.Expected.SignsTags ? tag.Expected.SigningKey : CommitSigning.None,
        CommitRequest or MergeRequest or RebaseRequest or RebaseContinueRequest or RebaseSkipRequest or RewriteRequest or RevertRequest or CherryPickRequest
            or PickContinueRequest => request.Expected.Signing,
        _ => CommitSigning.None
    };

    // A write cancelled while gpg-agent asked for the passphrase leaves its window open: the window is gpg-agent's.
    public static GitWriteResult Described(GitWriteRequest request, GitWriteResult result) =>
        result.Outcome == GitWriteOutcome.Cancelled && Key(request) is CommitSigning.OpenPgp or CommitSigning.X509
            ? result with { Message = result.Message + " " + WriteText.PassphraseWindowLeft } : result;
}
