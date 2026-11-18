using System.Text;
using Trag.Application.Ports.Git;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Accounts;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Write;

// A remote of the repository added, renamed, given another address or removed by Git's own remote commands, in the
// repository's configuration only; no server is asked. Names and addresses go after --, so neither is read as an option.
// The outcome is judged from the configuration afterwards: the remote under its name at the address asked, the old name
// gone, or, after an add or an edit, the remotes as they were when Git refused.
internal sealed class RemoteWriter(WriteCommandRunner git, IRepositoryReader reader)
{
    public Task<GitWriteResult> AddAsync(RemoteAddRequest request, RepositorySnapshot fresh, CancellationToken ct) =>
        RunAsync(fresh, [["remote", "add", "--", request.Name, request.Url]], request.Name, request.Url, null, _ => RemoteText.Added(request.Name), ct);

    public Task<GitWriteResult> EditAsync(RemoteEditRequest request, RepositorySnapshot fresh, CancellationToken ct)
    {
        var renamed = request.NewName != request.Name;
        List<string[]> commands = [];
        if (renamed) commands.Add(["remote", "rename", "--", request.Name, request.NewName]);
        if (request.Url is { } url) commands.Add(["remote", "set-url", "--", request.NewName, url]);
        // Git changes the address fetches use; a push URL of the remote's own stays, and is named.
        return RunAsync(fresh, commands, request.NewName, request.Url, renamed ? request.Name : null, after =>
            after.Remote(request.NewName) is { } saved && saved.PushAddress != saved.FetchAddress
                ? RemoteText.SavedPushingElsewhere(request.NewName, saved.PushAddress) : RemoteText.Saved(request.NewName), ct);
    }

    public Task<GitWriteResult> RemoveAsync(RemoteRemoveRequest request, RepositorySnapshot fresh, CancellationToken ct) =>
        RunAsync(fresh, [["remote", "remove", "--", request.Name]], null, null, request.Name, _ => RemoteText.Removed(request.Name), ct);

    // The commands run in order and stop at the first one Git refuses.
    private Task<GitWriteResult> RunAsync(RepositorySnapshot fresh, IReadOnlyList<string[]> commands, string? present, string? url, string? absent,
        Func<RepositorySnapshot, string> done, CancellationToken ct) =>
        VerifiedGitOperation.RunAsync(
            async token =>
            {
                GitResult? last = null;
                foreach (var command in commands)
                {
                    last = await git.RunAsync(fresh.Root, command, token).ConfigureAwait(false);
                    if (last.ExitCode != 0) break;
                }
                // Git leaves the account choices saved under the old name; nothing reads them any more.
                if (last!.ExitCode == 0 && absent is not null)
                    await git.RunAsync(fresh.Root, ["config", "--local", "--remove-section", RemoteAccountStore.Section(absent)], token).ConfigureAwait(false);
                return last;
            },
            async (command, failure, token) =>
            {
                var after = await reader.ReadAsync(fresh.Root, token).ConfigureAwait(false);
                var details = GitOutputText.Details(command);
                var placed = present is null || (after.Remote(present) is not null
                    && (url is null || await UrlAsync(fresh.Root, present, token).ConfigureAwait(false) == url));
                if (placed && after.Remote(absent) is null)
                    return new(GitWriteOutcome.Succeeded, done(after), details);
                // After an add or an edit the remotes as they were are Git's refusal only when Git ran to the end; a Git that was
                // stopped refused nothing. A removal Git takes off the upstream of every branch that tracks the remote before it
                // deletes its refs, and the remote last, so one that kept the remotes, stopped or refused, may have changed the
                // branches and refs already.
                if (present is not null && after.Remotes.Select(remote => remote.Name).SequenceEqual(fresh.Remotes.Select(remote => remote.Name)))
                    return failure is null ? GitWriteResult.Rejected(RemoteText.Failed, details) : new(GitWriteOutcome.Cancelled, RemoteText.Interrupted);
                return new(GitWriteOutcome.Uncertain, RemoteText.NotVerified, details);
            },
            (command, _) => new(GitWriteOutcome.Uncertain, RemoteText.NotVerified, GitOutputText.Details(command)),
            TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30), ct, new StoppedWriteLock([.. StoppedWriteLock.RefStorage(fresh.CommonDirectory!,
                [.. new[] { present, absent }.OfType<string>().Select(name => "refs/remotes/" + name + "/*"), "packed-refs"]), Path.Combine(fresh.CommonDirectory!, "config")]));

    private async Task<string?> UrlAsync(string root, string remote, CancellationToken ct)
    {
        var result = await git.RunAsync(root, ["config", "--local", "--get", "remote." + remote + ".url"], ct).ConfigureAwait(false);
        return result.ExitCode == 0 ? Encoding.UTF8.GetString(result.StandardOutput).TrimEnd('\r', '\n') : null;
    }
}
