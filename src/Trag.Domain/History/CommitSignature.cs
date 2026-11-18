namespace Trag.Domain.History;

// A commit's signature as Git checked it, and who signed it as the key's owner is known on this computer.
public sealed record CommitSignature(SignatureCheck Check, string Signer)
{
    public static readonly CommitSignature None = new(SignatureCheck.None, "");

    // Git's letter for its check (%G?), the signer it names (%GS) and what it said while checking. The letter reads a check
    // whose program Git could not start as unsigned (N, OpenPGP) or bad (B, SSH), and an SSH signature with no allowed
    // signers as unsigned; Git's own lines tell those apart: it cannot spawn a program on Windows, cannot run one it did
    // not find on the PATH and cannot exec one named by a path that is not there. Any other letter is a check that ran.
    public static CommitSignature FromGit(string letter, string signer, string errors) => new(
        letter is "N" or "B" && new[] { "cannot spawn", "cannot run", "cannot exec" }.Any(cause => errors.Contains(cause, StringComparison.Ordinal)) ? SignatureCheck.CheckerMissing
        : letter is "N" && errors.Contains("gpg.ssh.allowedSignersFile needs to be configured", StringComparison.Ordinal) ? SignatureCheck.NoAllowedSigners
        : letter switch
    {
        "G" => SignatureCheck.Good,
        "U" => SignatureCheck.Untrusted,
        "X" => SignatureCheck.Expired,
        "Y" => SignatureCheck.ExpiredKey,
        "R" => SignatureCheck.Revoked,
        "B" => SignatureCheck.Bad,
        "E" => SignatureCheck.CannotCheck,
        _ => SignatureCheck.None
    }, signer);
}
