using System.Security.Cryptography;
using System.Text;
using Trag.Domain.Accounts;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Git.Processes;
using Trag.Application.Ports.Catalog;

namespace Trag.Infrastructure.Accounts;

// Which account this repository uses for this remote: the accounts that exist, what the repository remembers, and the
// rule that turns the two into a choice. A selection is only saved if it is one the rule would offer. The repository
// remembers it in its own local Git configuration, under a key derived from the remote's name and the addresses it had,
// so a changed remote does not inherit the old choice.
public sealed class RemoteAccountStore(GitProcessRunner runner, IRemoteRepositoryDirectory accounts)
{
    public async Task<RemoteAccountState> ReadAsync(string root, string remote, string fetch, string push, CancellationToken ct)
    {
        var all = await accounts.AccountsAsync(ct).ConfigureAwait(false);
        var saved = await runner.RunAsync(root, ["config", "--local", "--no-includes", "--get", Key(remote, fetch, push)], ct).ConfigureAwait(false);
        if (saved.ExitCode is not (0 or 1)) throw new AccountException(AccountFailureText.SelectionNotRead);
        var savedId = saved.ExitCode == 0 ? Encoding.UTF8.GetString(saved.StandardOutput).TrimEnd('\r', '\n') : null;
        return RemoteAccountSelection.Choose(all, fetch, push, savedId);
    }

    public async Task SaveAsync(string root, string remote, string fetch, string push, string id, CancellationToken ct)
    {
        var state = await ReadAsync(root, remote, fetch, push, ct).ConfigureAwait(false);
        if (!state.Choices.Any(a => a.Id == id)) throw new AccountException(AccountFailureText.NoMatchingAccount);
        var result = await runner.RunAsync(root, ["config", "--local", "--replace-all", Key(remote, fetch, push), id], ct).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new AccountException(AccountFailureText.SelectionNotSaved);
    }

    private static string Key(string remote, string fetch, string push) => Section(remote) + ".choice" + Hash(fetch + "\0" + push);
    // Every choice saved for a remote of this name, whatever its addresses were.
    internal static string Section(string remote) => "trag-accounts." + Hash(remote);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
