using System.Text.Json;
using Trag.Infrastructure.Accounts.Credentials;
using Trag.Infrastructure.Platform.Secrets;

namespace Trag.Infrastructure.UnitTests.Accounts.Credentials;

public sealed class CredentialTests
{
    [Fact]
    public async Task WhenThePasswordIsAskedForAnotherHost_ShouldAnswerNothing()
    {
        using var ct = new CancellationTokenSource();
        await using var broker = new CredentialBroker(new("https://example.invalid"), "alice", "synthetic-value"u8, ct.Token);
        Assert.Null(broker.Authorize(new(broker.OperationId, "Password for 'https://alice@other.invalid': ")));
    }

    [Fact]
    public async Task WhenTheUsernameIsAskedAfterRefusedRequests_ShouldStillAnswerIt()
    {
        using var ct = new CancellationTokenSource();
        await using var broker = new CredentialBroker(new("https://example.invalid"), "alice", "synthetic-value"u8, ct.Token);
        RefusedThreeTimes(broker);
        Assert.Equal("alice"u8.ToArray(), broker.Authorize(new(broker.OperationId, "Username for 'https://example.invalid': ")));
    }

    [Fact]
    public async Task WhenTheOperationIsCancelled_ShouldAnswerNothing()
    {
        using var ct = new CancellationTokenSource();
        await using var broker = new CredentialBroker(new("https://example.invalid"), "alice", "synthetic-value"u8, ct.Token);
        RefusedThreeTimes(broker);
        Assert.Equal("alice"u8.ToArray(), broker.Authorize(new(broker.OperationId, "Username for 'https://example.invalid': ")));
        ct.Cancel();
        Assert.Null(broker.Authorize(new(broker.OperationId, "Password for 'https://alice@example.invalid': ")));
    }

    [Fact]
    public async Task WhenTheAskpassClientAsksForThePassword_ShouldGetTheSecretOverThePipeAndNeverThroughTheEnvironment()
    {
        await using var broker = new CredentialBroker(new("https://example.invalid"), "alice", "synthetic-value"u8, CancellationToken.None);
        using var output = new MemoryStream();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var code = await AskPassClient.RunAsync(broker.PipeName, broker.OperationId, "Password for 'https://alice@example.invalid': ", output, timeout.Token);
        Assert.Equal(0, code);
        Assert.Equal("synthetic-value\n"u8.ToArray(), output.ToArray());
        Assert.DoesNotContain(broker.CreateEnvironment("Trag.exe").Values, value => value?.Contains("synthetic-value", StringComparison.Ordinal) == true);
    }

    // A request on the pipe without its prompt, left out or null, is a malformed request, not one with no prompt to check.
    [Theory]
    [InlineData("{\"OperationId\":\"operation-1\"}")]
    [InlineData("{\"OperationId\":\"operation-1\",\"Prompt\":null}")]
    public void WhenARequestOnThePipeHasNoPrompt_ShouldRefuseItAsMalformed(string request) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(request, CredentialJsonContext.Default.CredentialRequest));

    [Fact]
    public async Task WhenTheWrittenBytesChangeAfterwards_ShouldKeepTheStoredSecretAsItWas()
    {
        using var store = new MemorySecretStore();
        var original = "synthetic"u8.ToArray();
        await store.WriteAsync("a", original, CancellationToken.None);
        original[0] = 0;
        var copy = await store.ReadAsync("a", CancellationToken.None);
        Assert.Equal("synthetic"u8.ToArray(), copy);
    }

    [Fact]
    public async Task WhenASecretIsDeleted_ShouldReadNothing()
    {
        using var store = new MemorySecretStore();
        var original = "synthetic"u8.ToArray();
        await store.WriteAsync("a", original, CancellationToken.None);
        original[0] = 0;
        var copy = await store.ReadAsync("a", CancellationToken.None);
        Assert.Equal("synthetic"u8.ToArray(), copy);
        await store.DeleteAsync("a", CancellationToken.None);
        Assert.Null(await store.ReadAsync("a", CancellationToken.None));
    }

    // A password for another host and one for another user were refused.
    private static void RefusedForAnotherHostAndUser(CredentialBroker broker)
    {
        Assert.Null(broker.Authorize(new(broker.OperationId, "Password for 'https://alice@other.invalid': ")));
        Assert.Null(broker.Authorize(new(broker.OperationId, "Password for 'https://bob@example.invalid': ")));
    }

    // ... and then a request from another operation.
    private static void RefusedThreeTimes(CredentialBroker broker)
    {
        RefusedForAnotherHostAndUser(broker);
        Assert.Null(broker.Authorize(new("wrong-operation", "Username for 'https://example.invalid': ")));
    }
}
