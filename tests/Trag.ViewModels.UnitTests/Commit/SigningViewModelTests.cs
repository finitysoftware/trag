using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.ViewModels.UnitTests.Commit;

public sealed class SigningViewModelTests
{
    // Cancel stops Git, but gpg-agent's passphrase window stays until someone closes it.
    [Fact]
    public async Task WhenACommitSigningWithGpgIsCancelled_ShouldSayThePassphraseWindowMayStillBeOpen()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Staged("a.txt")]) with { Signing = CommitSigning.OpenPgp };
        h.Writer.Result = new(GitWriteOutcome.Cancelled, CommitOutcomeText.Canceled);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Commit.CommitMessage = "Fix";

        await model.Commit.CommitCommand.ExecuteAsync(null);

        Assert.Equal(CommitOutcomeText.Canceled + " If a window still asks for your key's passphrase, close it: nothing waits for it any more.", model.Repository.Writer.LastWrite?.Result?.Message);
    }
}
