using Trag.Application.Ports.Accounts;
using Trag.Domain.Accounts;

namespace Trag.TestSupport;

public sealed class FakeAccountService : IAccountService
{
    public List<AccountInfo> Accounts { get; } = [];
    public SecretStoreStatus VaultStatus { get; set; } = SecretStoreStatus.Available;
    public Func<bool, IProgress<DeviceLoginProgress>, CancellationToken, Task<AccountInfo>>? SignInGitHub { get; set; }
    public Func<string, ReadOnlyMemory<byte>, bool, CancellationToken, Task<AccountInfo>>? SignInGitLab { get; set; }
    public Exception? RemoveFailure { get; set; }
    public List<string> Removed { get; } = [];

    public static AccountInfo GitHub(string id = "account-1", string user = "alice") =>
        new(id, AccountProvider.GitHub, "1", user, new("https://api.github.com/"), new("https://github.com/"), true, DateTimeOffset.UtcNow, null);
    public static AccountInfo GitLab(string id = "account-2", string user = "bob", string host = "https://gitlab.com/") =>
        new(id, AccountProvider.GitLab, "2", user, new(host + "api/v4/"), new(host), true, DateTimeOffset.UtcNow, null);

    public Task<AccountSettings> ReadAsync(CancellationToken ct) => Task.FromResult(new AccountSettings(Accounts.ToArray(), VaultStatus));
    public Task<AccountInfo> SignInGitHubAsync(bool memoryOnly, IProgress<DeviceLoginProgress> progress, CancellationToken ct) =>
        SignInGitHub?.Invoke(memoryOnly, progress, ct) ?? throw new NotSupportedException("SignInGitHub is not configured.");
    public Task<AccountInfo> SignInGitLabAsync(string instanceUrl, ReadOnlyMemory<byte> token, bool memoryOnly, CancellationToken ct) =>
        SignInGitLab?.Invoke(instanceUrl, token, memoryOnly, ct) ?? throw new NotSupportedException("SignInGitLab is not configured.");
    public Task<AccountInfo> VerifyAsync(string accountId, CancellationToken ct) => Task.FromResult(Accounts.Single(account => account.Id == accountId));
    public Task RemoveAsync(string accountId, CancellationToken ct)
    {
        if (RemoveFailure is not null) throw RemoveFailure;
        Removed.Add(accountId);
        Accounts.RemoveAll(account => account.Id == accountId);
        return Task.CompletedTask;
    }
}
