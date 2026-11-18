using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Writes;

// The key a write signs with, and what a cancelled one says when gpg-agent's passphrase window may still be open: the
// window is gpg-agent's, so cancelling the write does not close it. An SSH key asks for no window of its own.
public sealed class SigningWritesTests
{
    private const string Note = " If a window still asks for your key's passphrase, close it: nothing waits for it any more.";
    private static readonly RepositorySnapshot Gpg = Snapshots.Create() with { Signing = CommitSigning.OpenPgp, SigningKey = CommitSigning.OpenPgp, SignsTags = true };

    [Fact]
    public void WhenAWriteSigningWithGpgIsCancelled_ShouldSayTheWindowMayStillBeOpen()
    {
        var cancelled = new GitWriteResult(GitWriteOutcome.Cancelled, "Canceled.");
        GitWriteRequest[] requests = [new CommitRequest(Gpg, "m"), new MergeRequest(Gpg with { Signing = CommitSigning.X509 }, "f", Snapshots.Id('f'), false, "m"),
            new TagRequest(Gpg with { Signing = CommitSigning.None }, new("v1", Snapshots.Id('a'), "Release"))];

        Assert.All(requests, request => Assert.Equal("Canceled." + Note, SigningWrites.Described(request, cancelled).Message));
    }

    [Fact]
    public void WhenTheWriteSignsWithSshOrNothingOrWasNotCancelled_ShouldLeaveTheResultAsItIs()
    {
        var cancelled = new GitWriteResult(GitWriteOutcome.Cancelled, "Canceled.");
        Assert.Same(cancelled, SigningWrites.Described(new CommitRequest(Gpg with { Signing = CommitSigning.Ssh }, "m"), cancelled));
        Assert.Same(cancelled, SigningWrites.Described(new CommitRequest(Gpg with { Signing = CommitSigning.None }, "m"), cancelled));
        Assert.Same(cancelled, SigningWrites.Described(new TagRequest(Gpg with { SignsTags = false }, new("v1", Snapshots.Id('a'), "Release")), cancelled));
        var refused = new GitWriteResult(GitWriteOutcome.Rejected, "Refused.");
        Assert.Same(refused, SigningWrites.Described(new CommitRequest(Gpg, "m"), refused));
    }
}
