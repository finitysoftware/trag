using Trag.Application.Live;
using Trag.Application.Ports.Workspace;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;

namespace Trag.Application.Sync;

// The repository's remotes as its configuration has them, and the writes that add, change and remove one, through the
// repository's writer. What keeps an edit back is RemoteEdits' answer for the shown repository; a remote's address for
// editing is read as configured, since the list shows it shortened.
public sealed class RemoteSettingsModel(Repository repository, IBranchWorkspace? reader)
{
    public Repository Repository => repository;
    public IReadOnlyList<RepositoryRemote> Remotes => repository.Snapshot?.Remotes ?? [];

    public string AddError(string name, string url) => repository.Snapshot is { } snapshot ? RemoteEdits.AddError(snapshot, name.Trim(), url.Trim()) : "";
    public string EditError(string name, string newName, string? url) =>
        repository.Snapshot is { } snapshot ? RemoteEdits.EditError(snapshot, name, newName.Trim(), url?.Trim()) : "";

    // An edit changes something, and nothing keeps it back; url is null for an address left as it was.
    public bool CanEdit(string name, string newName, string? url) => EditError(name, newName, url).Length == 0 && (newName.Trim() != name || url is not null);

    // The address as configured, without a password or token it holds; the listed one when Git does not answer.
    public async Task<string> ReadUrlAsync(RepositoryRemote remote)
    {
        try
        {
            if (reader is not null && repository.Snapshot is { } snapshot && await reader.ReadRemoteUrlAsync(snapshot, remote.Name, CancellationToken.None) is { } configured)
                return RemoteUrls.WithoutCredentials(configured);
        }
        catch (Exception) { }
        return remote.FetchAddress;
    }

    // What removing the remote does to this repository; the current branch is named when it tracks that remote.
    public string Removal(string name) =>
        RemoteText.Removal(name, repository.Snapshot is { Branch: { } branch, TrackingRemote: { } tracking } && tracking == name ? branch : null);

    public Task<GitWriteResult?> AddAsync(string name, string url) =>
        repository.Writer.RunAsync(expected => new RemoteAddRequest(expected, name.Trim(), url.Trim()));
    // A remote just added brings its branches in, through the repository's fetch.
    public Task<GitWriteResult?> FetchAsync(string name) => repository.Remotes.FetchRemoteAsync(name.Trim());
    public Task<GitWriteResult?> EditAsync(string name, string newName, string? url) =>
        repository.Writer.RunAsync(expected => new RemoteEditRequest(expected, name, newName.Trim(), url?.Trim()));
    public Task<GitWriteResult?> RemoveAsync(string name) => repository.Writer.RunAsync(expected => new RemoteRemoveRequest(expected, name));
}
