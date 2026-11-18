namespace Trag.Domain.Status;

public static class CommitSignings
{
    // Whether commit.gpgSign is on, and the key the one gpg.format names.
    public static CommitSigning Of(bool signs, string? format) => signs ? Key(format) : CommitSigning.None;

    // The kind of key Git signs with whatever it signs: OpenPGP unless gpg.format says otherwise.
    public static CommitSigning Key(string? format) => format switch
    {
        "ssh" => CommitSigning.Ssh,
        "x509" => CommitSigning.X509,
        _ => CommitSigning.OpenPgp
    };

    // Git that could not write a commit object while it signs commits could not sign it; a missing signing program says no
    // more than that, so the reason is not looked for in the rest of Git's output.
    public static bool Refused(CommitSigning signing, string details) =>
        signing != CommitSigning.None && details.Contains("failed to write commit object", StringComparison.Ordinal);

    // A key may ask for its passphrase, typed at a person's pace: a write that makes commits Git signs waits as a commit
    // does, with Cancel as the way out; unsigned it keeps the limit it has.
    public static TimeSpan? Limit(CommitSigning signing, TimeSpan limit) => signing == CommitSigning.None ? limit : null;

    // Git's own line for a tag it could not sign, whichever setting asked for the signature.
    public static bool TagRefused(string details) => details.Contains("unable to sign the tag", StringComparison.Ordinal);

    // Git's own line for a tag it wanted a message for, as a tag it signs does, and got none.
    public static bool TagNeedsMessage(string details) => details.Contains("no tag message?", StringComparison.Ordinal);
}
