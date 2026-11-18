using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Write;

// A remote added or edited against real Git when Cancel arrives before Git writes anything.
public sealed class RemoteChangeTests
{
    // Git writes an added remote, a remote's new name and its new address each in one step of the configuration, so the
    // remotes as they were after a stop mean that nothing changed. A removal changes the branches that track the remote
    // first, so it is not judged so.
    [Theory]
    [InlineData("add")]
    [InlineData("edit")]
    public async Task WhenCancelArrivesBeforeGitRuns_ShouldSayTheChangeWasInterruptedBeforeAnythingChanged(string change)
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.CommitAsync("first");
        await fixture.GitAsync("remote", "add", "origin", "https://example.invalid/origin.git");
        var snapshot = await fixture.ReadAsync();
        var writer = new RemoteWriter(new WriteCommandRunner(new GitProcessRunner()), new CliRepositoryReader(fixture.Runner));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var result = change == "add"
            ? await writer.AddAsync(new RemoteAddRequest(snapshot, "upstream", "https://example.invalid/upstream.git"), snapshot, cancellation.Token)
            : await writer.EditAsync(new RemoteEditRequest(snapshot, "origin", "source", "https://example.invalid/source.git"), snapshot, cancellation.Token);

        Assert.Equal((GitWriteOutcome.Cancelled, RemoteText.Interrupted), (result.Outcome, result.Message));
        Assert.Equal("origin", await fixture.GitAsync("remote"));
    }
}
