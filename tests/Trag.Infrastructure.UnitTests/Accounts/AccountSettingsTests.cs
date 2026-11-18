using System.Text.Json;
using Trag.Domain.Accounts;
using Trag.Domain.Errors;
using Trag.Infrastructure.Accounts;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Accounts;

// The settings file and the limits around it: what may be saved, how many accounts there may be, what an unreadable
// file does, and what is left behind when saving the metadata fails after the credential was already written.
public sealed class AccountSettingsTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task WhenAHundredAccountsSignIn_ShouldKeepThemAll()
    {
        using var f = new Fixture();
        using var service = f.ServiceWithoutFile();
        await AHundredSignInsAsync(service);
        Assert.Equal(100, (await service.ReadAsync(Ct)).Accounts.Count);
    }

    [Fact]
    public async Task WhenTheHundredAndFirstAccountSignsIn_ShouldRefuseItAndKeepTheHundred()
    {
        using var f = new Fixture();
        using var service = f.ServiceWithoutFile();
        await AHundredSignInsAsync(service);
        Assert.Equal(100, (await service.ReadAsync(Ct)).Accounts.Count);
        Assert.Equal("Trag supports up to 100 accounts. Remove an unused account before adding another.",
            (await Assert.ThrowsAsync<AccountException>(() => service.SignInGitLabAsync("https://gitlab.example", "token"u8.ToArray(), false, Ct))).Message);
        Assert.Equal(100, (await service.ReadAsync(Ct)).Accounts.Count);
    }

    [Fact]
    public async Task WhenTheSameIdentitySignsInAgain_ShouldReplaceItInsteadOfCountingTwice()
    {
        using var f = new Fixture();
        using var service = f.Service(identity: _ => "{\"id\":1,\"username\":\"alice\"}");
        await service.SignInGitLabAsync("https://gitlab.example", "token"u8.ToArray(), false, Ct);
        await service.SignInGitLabAsync("https://gitlab.example", "token"u8.ToArray(), false, Ct);
        Assert.Single((await service.ReadAsync(Ct)).Accounts);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(2)]
    public async Task WhenTheFileHasTooManyAccountsOrARepeatedId_ShouldRefuseItAsAWhole(int count)
    {
        using var f = new Fixture();
        // 101 valid accounts, or two entries with the same id: either way the file is not trusted in part.
        var accounts = Enumerable.Range(0, count).Select(i => Account(count == 2 ? 0 : i)).ToArray();
        await WriteDocumentAsync(f.Path, accounts);
        using var service = f.Service();
        Assert.Equal("Account settings could not be read. Check access to the Trag settings folder before retrying.",
            (await Assert.ThrowsAsync<AccountException>(() => service.ReadAsync(Ct))).Message);
    }

    [Fact]
    public async Task WhenTheFileHasExactlyAHundredAccounts_ShouldStillReadThem()
    {
        using var f = new Fixture();
        await WriteDocumentAsync(f.Path, [.. Enumerable.Range(0, 100).Select(Account)]);
        using var service = f.Service();
        Assert.Equal(100, (await service.ReadAsync(Ct)).Accounts.Count);
    }

    [Fact]
    public async Task WhenTheSettingsCannotBeSavedAfterASignIn_ShouldRefuseItAndLeaveNoCredentialBehind()
    {
        using var f = new Fixture();
        // The settings folder is a file, so creating it fails and the save after Store throws.
        var blocked = Path.Combine(f.Root, "blocked");
        Directory.CreateDirectory(f.Root);
        await File.WriteAllTextAsync(blocked, "not a folder\n", Ct);
        using var service = f.Service(Path.Combine(blocked, "accounts.json"));
        Assert.Equal("Account settings could not be saved. Check access to the Trag settings folder and retry.",
            (await Assert.ThrowsAsync<AccountException>(() => service.SignInGitLabAsync("https://gitlab.example", "token"u8.ToArray(), false, Ct))).Message);
        var id = AccountIdentity.StableId(AccountProvider.GitLab, AccountMapping.GitLabApiBase("https://gitlab.example"), "1");
        Assert.Null(await f.Vault.ReadAsync(id, Ct));
        Assert.Empty((await service.ReadAsync(Ct)).Accounts);
    }

    private static AccountInfo Account(int index)
    {
        var api = AccountMapping.GitLabApiBase("https://gitlab.example");
        var userId = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new(AccountIdentity.StableId(AccountProvider.GitLab, api, userId), AccountProvider.GitLab, userId,
            "user-" + userId, api, new(api, "../../"), true, DateTimeOffset.UnixEpoch, null);
    }

    [Fact]
    public async Task WhenASaveIsCancelled_ShouldKeepThePreviousSettingsAndLeaveNoTemporaryFile()
    {
        using var f = new Fixture();
        await WriteDocumentAsync(f.Path, [Account(1)]);
        var file = new AccountSettingsFile(f.Path);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => file.UpdateAsync(saved => [.. saved, Account(2)], cancelled.Token));

        Assert.Single((await file.LoadAsync(Ct))!);
        Assert.Empty(Directory.GetFiles(f.Root, "*.tmp"));
    }

    // A hundred different identities on the same GitLab.
    private static async Task AHundredSignInsAsync(AccountService service)
    {
        for (var i = 0; i < 100; i++) await service.SignInGitLabAsync("https://gitlab.example", "token"u8.ToArray(), false, Ct);
    }

    private static async Task WriteDocumentAsync(string path, AccountInfo[] accounts)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(
            new AccountDocument(accounts), AccountJsonContext.Default.AccountDocument), Ct);
    }

    private sealed class Fixture : IDisposable
    {
        private int identities;
        public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "trag-accounts-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Root, "accounts.json");
        public FakeSecretStore Vault { get; } = new();

        public AccountService Service(string? settingsPath = null, Func<int, string>? identity = null) =>
            new(settingsPath ?? Path, Vault, new(new AccountServiceTests.Handler((_, _) =>
                AccountServiceTests.Reply(identity is null ? "{\"id\":" + ++identities + ",\"username\":\"user-" + identities + "\"}" : identity(++identities)))));

        // No settings file at all: Save does nothing, so a hundred sign-ins cost nothing on disk.
        public AccountService ServiceWithoutFile() => new(null, Vault, new(new AccountServiceTests.Handler((_, _) =>
            AccountServiceTests.Reply("{\"id\":" + ++identities + ",\"username\":\"user-" + identities + "\"}"))));

        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
