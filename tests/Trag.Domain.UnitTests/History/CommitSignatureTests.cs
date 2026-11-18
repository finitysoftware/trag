using Trag.Domain.History;
using Trag.Domain.Presentation.Text;

namespace Trag.Domain.UnitTests.History;

// A commit's signature as Git checked it (%G?, %GS) and how the commit pane says it. Git answers N both for no signature
// and for an SSH signature it has no allowed signers to check against, so neither says anything.
public sealed class CommitSignatureTests
{
    [Theory]
    [InlineData("G", SignatureCheck.Good)]
    [InlineData("U", SignatureCheck.Untrusted)]
    [InlineData("X", SignatureCheck.Expired)]
    [InlineData("Y", SignatureCheck.ExpiredKey)]
    [InlineData("R", SignatureCheck.Revoked)]
    [InlineData("B", SignatureCheck.Bad)]
    [InlineData("E", SignatureCheck.CannotCheck)]
    [InlineData("N", SignatureCheck.None)]
    [InlineData("", SignatureCheck.None)]
    public void WhenGitAnswersItsCheck_ShouldKeepWhatItFound(string letter, SignatureCheck expected) =>
        Assert.Equal(new CommitSignature(expected, "ana@example.invalid"), CommitSignature.FromGit(letter, "ana@example.invalid", ""));

    // Git's letter alone reads a check that could not run as unsigned (OpenPGP) or bad (SSH), and an SSH signature with no
    // allowed signers as unsigned; what Git said while checking tells them apart.
    [Theory]
    [InlineData("N", "error: cannot spawn C:/nowhere/gpg.exe: No such file or directory", SignatureCheck.CheckerMissing)]
    [InlineData("B", "error: cannot run ssh-keygen: No such file or directory", SignatureCheck.CheckerMissing)]
    [InlineData("B", "error: cannot exec '/opt/nowhere/ssh-keygen': No such file or directory", SignatureCheck.CheckerMissing)]
    [InlineData("N", "error: gpg.ssh.allowedSignersFile needs to be configured and exist for ssh signature verification", SignatureCheck.NoAllowedSigners)]
    public void WhenGitSaysWhyItCouldNotCheck_ShouldSaySoInsteadOfItsLetter(string letter, string errors, SignatureCheck expected) =>
        Assert.Equal(expected, CommitSignature.FromGit(letter, "", errors).Check);

    // A check that ran answers with its own letter; a line about some other program Git could not start does not undo it.
    [Theory]
    [InlineData("G", SignatureCheck.Good)]
    [InlineData("E", SignatureCheck.CannotCheck)]
    public void WhenTheCheckRanButGitSaysSomethingElseFailed_ShouldKeepWhatTheCheckFound(string letter, SignatureCheck expected) =>
        Assert.Equal(expected, CommitSignature.FromGit(letter, "ana@example.invalid",
            "error: cannot run some-helper: No such file or directory\nerror: gpg.ssh.allowedSignersFile needs to be configured and exist for ssh signature verification").Check);

    [Theory]
    [InlineData(SignatureCheck.Good, "Signed by ana@example.invalid.")]
    [InlineData(SignatureCheck.Untrusted, "Signed by ana@example.invalid, whose key this computer does not trust.")]
    [InlineData(SignatureCheck.Expired, "Signed by ana@example.invalid; the signature has expired.")]
    [InlineData(SignatureCheck.ExpiredKey, "Signed by ana@example.invalid with a key that has expired.")]
    [InlineData(SignatureCheck.Revoked, "Signed by ana@example.invalid with a key that was revoked.")]
    [InlineData(SignatureCheck.Bad, "Git reports a bad signature: the commit does not match what was signed.")]
    [InlineData(SignatureCheck.CheckerMissing, "Signed, but Git could not start the program that checks signatures. Check gpg.program, or gpg.ssh.program for an SSH key.")]
    [InlineData(SignatureCheck.NoAllowedSigners, "Signed with an SSH key, but Git has no list of allowed signers to check it against (gpg.ssh.allowedSignersFile).")]
    [InlineData(SignatureCheck.CannotCheck, "Signed, but this computer cannot check the signature: it does not have the key.")]
    [InlineData(SignatureCheck.NotFinished, "Trag stopped waiting for Git to check the signature after 30 seconds. Check gpg.program, or gpg.ssh.program for an SSH key.")]
    [InlineData(SignatureCheck.None, "")]
    public void WhenTheCommitPaneShowsTheSignature_ShouldSayWhatGitFound(SignatureCheck check, string expected) =>
        Assert.Equal(expected, CommitText.Signature(new(check, "ana@example.invalid")));
}
