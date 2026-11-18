using Trag.Infrastructure.Accounts.Credentials;

namespace Trag.IntegrationTests.Accounts;

// A broker lives as long as the transfer it was made for. A recursive submodule update clones a large library first and
// its nested submodules after it, so their prompts can arrive minutes into the same operation. The test waits out the
// broker's two minutes on the real clock, which is why it lives with the slow tests.
public sealed class CredentialBrokerLifetimeTests
{
    [Fact]
    public async Task WhenTheOperationIsStillRunningAfterTwoMinutes_ShouldStillAnswerItsPrompts()
    {
        using var operation = new CancellationTokenSource();
        await using var broker = new CredentialBroker(new("https://example.invalid/"), "alice", "synthetic-value"u8, operation.Token);
        Assert.NotNull(broker.Authorize(new(broker.OperationId, "Password for 'https://alice@example.invalid/lib/large.git': ")));

        await Task.Delay(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(5));

        Assert.False(operation.IsCancellationRequested);
        Assert.NotNull(broker.Authorize(new(broker.OperationId, "Password for 'https://alice@example.invalid/lib/nested.git': ")));
    }
}
