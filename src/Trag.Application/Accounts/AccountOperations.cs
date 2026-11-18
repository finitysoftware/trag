using Trag.Application.Ports.Accounts;
using Trag.Domain.Accounts;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;

namespace Trag.Application.Accounts;

// The account settings' operations as outcomes: each changes the accounts through the service, reads the settings again
// when it changed them, and says in one sentence how it ended. A cancellation (one the service did not observe included),
// an account error and any other failure are outcomes too, so the view model never catches.
public sealed class AccountOperations(IAccountService service)
{
    public Task<AccountOutcome> LoadAsync(CancellationToken ct) => RunAsync(() => ReadAsync(AccountText.Loaded, ct), ct);

    public Task<AccountOutcome> SignInGitHubAsync(bool memoryOnly, IProgress<DeviceLoginProgress> progress, CancellationToken ct) =>
        RunAsync(async () => await ConnectedAsync(await service.SignInGitHubAsync(memoryOnly, progress, ct), ct), ct);

    // The token stays between the view and the service; only the account that came of it enters here.
    public Task<AccountOutcome> SignInAsync(Func<CancellationToken, Task<AccountInfo>> signIn, CancellationToken ct) =>
        RunAsync(async () => await ConnectedAsync(await signIn(ct), ct), ct);

    public Task<AccountOutcome> VerifyAsync(string accountId, CancellationToken ct) =>
        RunAsync(async () => await ReadAsync(AccountText.Verified(await service.VerifyAsync(accountId, ct)), ct), ct);

    public async Task<AccountOutcome> RemoveAsync(string accountId, CancellationToken ct)
    {
        var removed = await RunAsync(async () =>
        {
            await service.RemoveAsync(accountId, ct);
            return new AccountOutcome(null, AccountText.Removed);
        }, ct);
        if (!removed.Succeeded) return removed;
        return (await RunAsync(() => ReadAsync(AccountText.Removed, ct), ct)) with { Removed = true };
    }

    private async Task<AccountOutcome> ConnectedAsync(AccountInfo account, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return (await ReadAsync(AccountText.Connected(account), ct)) with { SignedIn = true };
    }

    private async Task<AccountOutcome> ReadAsync(string status, CancellationToken ct)
    {
        var settings = await service.ReadAsync(ct);
        ct.ThrowIfCancellationRequested();
        return new(settings, status);
    }

    private static async Task<AccountOutcome> RunAsync(Func<Task<AccountOutcome>> work, CancellationToken ct)
    {
        try
        {
            var outcome = await work();
            ct.ThrowIfCancellationRequested();
            return outcome;
        }
        catch (OperationCanceledException) { return AccountOutcome.Failure(AccountText.Canceled); }
        catch (AccountException error) { return AccountOutcome.Failure(error.Message); }
        catch (Exception) { return AccountOutcome.Failure(AccountText.Failed); }
    }
}
