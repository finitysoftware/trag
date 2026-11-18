using Trag.Domain.Accounts;
using Trag.Domain.Status;

namespace Trag.Domain.Branches;

// The provider's page that starts a pull request (GitHub) or a merge request (GitLab) from a local branch, on the remote
// its upstream lives on and by the branch's name there. The page is made from the remote's push address, HTTPS, ssh://
// or git@host:path, keeping only the host and the project's path: no credentials, and no SSH port, which is not the
// web's. GitHub is github.com; GitLab is gitlab.com or a host named gitlab.something, as a self-hosted one usually is.
// Their SSH hosts on the HTTPS port, ssh.github.com and altssh.gitlab.com, stand for github.com and gitlab.com.
// The provider comes with the page, from the same remote, so what a menu calls the request is what the page starts.
public sealed record PullRequestPage(AccountProvider Provider, Uri Address)
{
    private const string RemoteRefs = "refs/remotes/";

    public static PullRequestPage? For(BranchInfo branch, IReadOnlyList<RepositoryRemote> remotes)
    {
        if (branch.Upstream is not { } upstream) return null;
        // A remote may be named with a slash, so the upstream belongs to the longest remote its name starts with.
        var remote = remotes.Where(remote => upstream.StartsWith(RemoteRefs + remote.Name + "/", StringComparison.Ordinal)).MaxBy(remote => remote.Name.Length);
        if (remote is null || Project(remote.PushAddress) is not { } project) return null;
        var name = upstream[(RemoteRefs.Length + remote.Name.Length + 1)..];
        return ProviderAt(project.Host) switch
        {
            AccountProvider.GitHub => new(AccountProvider.GitHub, new Uri(project.Page + "/compare/" + string.Join('/', name.Split('/').Select(Uri.EscapeDataString)) + "?expand=1")),
            AccountProvider.GitLab => new(AccountProvider.GitLab, new Uri(project.Page + "/-/merge_requests/new?merge_request%5Bsource_branch%5D=" + Uri.EscapeDataString(name))),
            _ => null
        };
    }

    public static AccountProvider? ProviderOf(string address) => Project(address) is { } project ? ProviderAt(project.Host) : null;

    private static AccountProvider? ProviderAt(string host) => host.ToLowerInvariant() switch
    {
        "github.com" => AccountProvider.GitHub,
        "gitlab.com" => AccountProvider.GitLab,
        var other when other.StartsWith("gitlab.", StringComparison.Ordinal) => AccountProvider.GitLab,
        _ => null
    };

    // The host and the project's web page, from https://[user@]host[:port]/path, ssh://[user@]host[:port]/path or
    // [user@]host:path (the snapshot hides the user); a project has an owner or group and a name.
    private static (string Host, string Page)? Project(string address)
    {
        string host, port, path;
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "ssh")
            (host, port, path) = (uri.Host, uri.Scheme == "https" && !uri.IsDefaultPort ? ":" + uri.Port : "", uri.AbsolutePath);
        else if (RemoteAddress.IsScpLike(address) && address.IndexOf(':') is var colon)
            (host, port, path) = (address[(address.LastIndexOf('@', colon) + 1)..colon], "", address[(colon + 1)..]);
        else return null;
        host = host.ToLowerInvariant() switch { "ssh.github.com" => "github.com", "altssh.gitlab.com" => "gitlab.com", _ => host };
        path = RemoteAddress.ProjectPath(path);
        return path.Split('/').Length < 2 ? null : (host, "https://" + host + port + "/" + path);
    }
}
