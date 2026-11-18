using Trag.Domain.Errors;
using Trag.Infrastructure.Accounts;
using Trag.Infrastructure.Accounts.Credentials;
using Trag.Infrastructure.Git.Catalog;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Sync;

namespace Trag.Infrastructure.UnitTests.Git.Catalog;

// The account the clone of the repository itself uses answers for the repository's own address; the submodules, cloned
// afterwards, each ask for the whole host with credentials of their own.
public sealed class CloneCredentialTests
{
    // Records the address the account was asked for and stops the clone there, before Git starts.
    private sealed class RecordingBrokers : ICredentialBrokerSource
    {
        public List<Uri> Endpoints { get; } = [];

        public Task<CredentialBroker> CreateBrokerAsync(string accountId, Uri endpoint, CancellationToken ct)
        {
            Endpoints.Add(endpoint);
            throw new AccountException("Stopped before Git.");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenAnAccountClones_ShouldAnswerForTheRepositorysOwnAddress(bool submodules)
    {
        var brokers = new RecordingBrokers();
        var runner = new GitProcessRunner();
        var clone = new GitCloneOperation(runner, new(runner), brokers, "askpass.exe", SyncTimeouts.Quiet);
        var target = Path.Combine(Path.GetTempPath(), "trag-clone-" + Guid.NewGuid().ToString("N"));

        var result = await clone.CloneAsync(new("https://example.invalid/team/project.git", target, "account", Submodules: submodules), CancellationToken.None);

        Assert.Equal("Stopped before Git.", result.Message);
        Assert.Equal([new Uri("https://example.invalid/team/project.git")], brokers.Endpoints);
    }
}
