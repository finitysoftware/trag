using Trag.Domain.Accounts;
using Trag.Infrastructure.Platform.Secrets;

namespace Trag.IntegrationTests.Accounts;

// The Windows credential store itself: what it refuses before calling the OS, and what "not there" looks like as
// opposed to "not available". Every test writes under its own GUID target and removes it again.
// The calls are started before the assertion so that every call site stays inside the Windows guard.
public sealed class WindowsVaultTests
{
    private static readonly CancellationToken Ct = new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token;

    [Fact]
    public async Task WhenASecretIsLargerThanTheStoreAccepts_ShouldRefuseItBeforeTheOsIsCalled()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new WindowsSecretStore();
        var id = "test-" + Guid.NewGuid().ToString("N");
        var write = store.WriteAsync(id, new byte[2561], Ct);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => write);
        Assert.Null(await store.ReadAsync(id, Ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has\ncontrol")]
    public async Task WhenTheStoreCannotNameTheIdentifier_ShouldRefuseEveryCall(string id)
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new WindowsSecretStore();
        var read = store.ReadAsync(id, Ct);
        var write = store.WriteAsync(id, new byte[8], Ct);
        var delete = store.DeleteAsync(id, Ct);
        await Assert.ThrowsAsync<ArgumentException>(() => read);
        await Assert.ThrowsAsync<ArgumentException>(() => write);
        await Assert.ThrowsAsync<ArgumentException>(() => delete);
    }

    [Fact]
    public async Task WhenTheIdentifierIsLongerThanTwoHundredCharacters_ShouldRefuseIt()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new WindowsSecretStore();
        var read = store.ReadAsync(new string('a', 201), Ct);
        await Assert.ThrowsAsync<ArgumentException>(() => read);
        Assert.Null(await store.ReadAsync(new string('a', 200), Ct));
    }

    [Fact]
    public async Task WhenTheCredentialIsMissing_ShouldReadNothingRatherThanReportAnUnavailableStore()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new WindowsSecretStore();
        var id = "test-" + Guid.NewGuid().ToString("N");
        Assert.Null(await store.ReadAsync(id, Ct));
        await store.DeleteAsync(id, Ct);
        Assert.Equal(SecretStoreStatus.Available, await store.GetStatusAsync(Ct));
    }

    [Fact]
    public async Task WhenASecretIsWrittenThenDeleted_ShouldReadItBackAndThenBeGone()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new WindowsSecretStore();
        var id = "test-" + Guid.NewGuid().ToString("N");
        try
        {
            await store.WriteAsync(id, "credential-value"u8.ToArray(), Ct);
            Assert.Equal("credential-value"u8.ToArray(), await store.ReadAsync(id, Ct));
            await store.WriteAsync(id, new byte[2560], Ct);
            Assert.Equal(2560, (await store.ReadAsync(id, Ct))!.Length);
        }
        finally { await store.DeleteAsync(id, Ct); }
        Assert.Null(await store.ReadAsync(id, Ct));
    }
}
