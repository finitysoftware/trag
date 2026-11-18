using Trag.Domain.Accounts;

namespace Trag.Application.Ports.Accounts;

public interface IAccountService
{
    Task<AccountSettings> ReadAsync(CancellationToken ct);
    Task<AccountInfo> SignInGitHubAsync(bool memoryOnly, IProgress<DeviceLoginProgress> progress, CancellationToken ct);
    // Called directly by the secure entry view, never bound to a ViewModel property.
    Task<AccountInfo> SignInGitLabAsync(string instanceUrl, ReadOnlyMemory<byte> token, bool memoryOnly, CancellationToken ct);
    Task<AccountInfo> VerifyAsync(string accountId, CancellationToken ct);
    Task RemoveAsync(string accountId, CancellationToken ct);
}
