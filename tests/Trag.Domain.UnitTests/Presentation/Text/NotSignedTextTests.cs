using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Domain.UnitTests.Presentation.Text;

// What a write Git could not sign says: the key and what to check about it, then where the operation was left.
public sealed class NotSignedTextTests
{
    private const string SshKey = "Git could not sign the commit with your SSH key. A key with a passphrase has to be in an ssh-agent the signing program reaches: for Windows' own agent, set gpg.ssh.program to C:/Windows/System32/OpenSSH/ssh-keygen.exe. ";

    [Fact]
    public void WhenAMergeCommitCouldNotBeSigned_ShouldSayTheMergeIsReadyToFinishOrAbort() =>
        Assert.Equal(SshKey + "The merge is ready: finish it once the key can sign, or abort it.", MergeOutcomeText.NotSigned(CommitSigning.Ssh));

    [Fact]
    public void WhenACherryPickCouldNotBeSigned_ShouldSayItIsReadyToFinishOrAbort() =>
        Assert.Equal(SshKey + "The cherry-pick is ready: finish it once the key can sign, or abort it.", PickText.NotSigned(RepositoryOperation.CherryPick, CommitSigning.Ssh));

    [Fact]
    public void WhenARevertCouldNotBeSignedAndWasPutBack_ShouldSayNothingChanged() =>
        Assert.Equal(SshKey + "Nothing was changed.", PickText.NotSignedUndone(CommitSigning.Ssh));

    [Fact]
    public void WhenATagCouldNotBeSigned_ShouldSayNoTagWasCreated() =>
        Assert.Equal("Git could not sign the tag with your SSH key. A key with a passphrase has to be in an ssh-agent the signing program reaches: for Windows' own agent, set gpg.ssh.program to C:/Windows/System32/OpenSSH/ssh-keygen.exe. No tag was created.",
            TagOutcomeText.NotSigned(CommitSigning.Ssh));

    [Fact]
    public void WhenARebaseCouldNotSignACommit_ShouldSayItStoppedToContinueOrAbort() =>
        Assert.Equal(SshKey + "The rebase stopped: continue it once the key can sign, or abort it.", RebaseText.NotSigned(CommitSigning.Ssh));
}
