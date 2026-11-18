using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Domain.Naming;

// What keeps adding, changing or removing a remote of this repository back before Git is asked; empty when nothing
// does. The Remotes dialog asks it to enable its buttons, and the validator asks it again of the request.
public static class RemoteEdits
{
    public static string AddError(RepositorySnapshot snapshot, string name, string url) =>
        First(RemoteNames.Error(name, snapshot.Remotes.Select(remote => remote.Name)), UrlError(url));

    // Url null keeps the remote's address. A kept name is not judged again, so a remote that already shares its
    // remote-tracking branches with another one can still get a new address.
    public static string EditError(RepositorySnapshot snapshot, string name, string newName, string? url) =>
        snapshot.Remote(name) is null ? RemoteText.Gone
        : First(newName == name ? "" : RemoteNames.Error(newName, snapshot.Remotes.Select(remote => remote.Name).Where(other => other != name)),
            url is null ? "" : UrlError(url));

    public static string RemoveError(RepositorySnapshot snapshot, string name) => snapshot.Remote(name) is null ? RemoteText.Gone : "";

    private static string UrlError(string url) =>
        url.Trim().Length == 0 ? RemoteText.UrlMissing
        : !RemoteUrls.IsValidAddress(url) ? RemoteText.UrlInvalid
        : RemoteUrls.HasCredentials(url) ? RemoteText.UrlHasPassword : "";

    private static string First(string error, string next) => error.Length != 0 ? error : next;
}
