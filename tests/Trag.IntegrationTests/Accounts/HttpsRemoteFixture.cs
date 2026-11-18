using System.Net;
using System.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Accounts;
using Trag.Infrastructure.Git.Catalog;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Sync;
using Trag.Infrastructure.Platform.Storage;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Accounts;

// A GitFixture whose repositories are served over HTTPS behind Basic authentication as team/<name>.git, trusted by
// every Git the process starts while the fixture lives, with the accounts a test signs in to that server as a GitLab
// instance and Trag's own executable as Git's askpass helper.
internal sealed class HttpsRemoteFixture : IDisposable
{
    private static readonly string AskPass = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Trag.exe" : "Trag");
    private readonly IDisposable trust;

    private HttpsRemoteFixture(GitFixture git, string userName, string password)
    {
        Git = git;
        Directory.CreateDirectory(Served);
        Server = new(Served, userName, password);
        trust = Server.TrustedByGit();
    }

    public GitFixture Git { get; }
    public HttpsGitServer Server { get; }
    public string Container => Git.Container;
    public CancellationToken Token => Git.Token;
    private string Served => Path.Combine(Git.Container, "served");

    public static async Task<HttpsRemoteFixture> CreateAsync(string userName, string password, TimeSpan timeout) =>
        new(await GitFixture.CreateAsync("main", timeout), userName, password);

    public string Bare(string name) => Path.Combine(Served, "team", name + ".git");

    // A repository with a file of the name's first letter, and the submodule if one is named, committed and served as
    // team/<name>.git. The submodule is the repository of that name beside the fixture's, listed under its address.
    public async Task PublishAsync(string repository, string name, string? submodule = null, string? submoduleUrl = null)
    {
        if (repository != Git.Root)
        {
            Directory.CreateDirectory(repository);
            await Git.GitAtAsync(repository, "init", "--initial-branch=main");
            await SubmoduleFixture.IdentifyAsync(Git, repository);
        }
        var file = name[..1] + ".txt";
        await File.WriteAllTextAsync(Path.Combine(repository, file), name[..1] + "\n");
        await Git.GitAtAsync(repository, "add", "--", file);
        if (submodule is not null)
        {
            await Git.GitAtAsync(repository, "-c", "protocol.file.allow=always", "submodule", "add", "--", Path.Combine(Git.Container, submodule), submodule);
            await Git.GitAtAsync(repository, "config", "--file", ".gitmodules", "submodule." + submodule + ".url", submoduleUrl!);
            await Git.GitAtAsync(repository, "add", "--", ".gitmodules");
        }
        await Git.GitAtAsync(repository, "commit", "-m", name);
        await Git.GitAtAsync(Git.Container, "clone", "--bare", "--", repository, Bare(name));
    }

    // The fixture's repository has lib as a submodule, and lib has nested, each by its address on the server.
    public async Task PublishWithNestedSubmoduleAsync()
    {
        await PublishAsync(Path.Combine(Git.Container, "nested"), "nested");
        await PublishAsync(Path.Combine(Git.Container, "lib"), "lib", "nested", Server.Url("team/nested.git"));
        await PublishAsync(Git.Root, "project", "lib", Server.Url("team/lib.git"));
    }

    // Each user signed in with a token; the provider is asked only who a token belongs to.
    public async Task<AccountService> SignInAsync(params (string Name, string Token)[] users)
    {
        var accounts = new AccountService(Path.Combine(Git.Container, "accounts.json"), new FakeSecretStore(), new(new IdentityHandler(users)));
        foreach (var (_, token) in users)
            await accounts.SignInGitLabAsync(Server.Url("").TrimEnd('/'), Encoding.UTF8.GetBytes(token), false, Token);
        return accounts;
    }

    public GitSyncFacade Sync(AccountService? accounts = null) =>
        new(Git.Runner, new CliRepositoryReader(Git.Runner), accounts: accounts, askPassExecutable: AskPass);

    public RepositoryCatalogFacade Catalog(AccountService? accounts = null) =>
        new(Git.Runner, new RepositoryCatalogStore(Path.Combine(Git.Container, "repositories.json")), accounts, AskPass);

    public async Task<GitWriteResult> ExecuteAsync(GitSyncFacade sync, SyncAction action, string? accountId = null)
    {
        var snapshot = await Git.ReadAsync();
        var state = await sync.ReadAsync(snapshot, "origin", null, Token);
        return await sync.ExecuteAsync(new SyncRequest(snapshot, state, action, accountId), Token);
    }

    public void Dispose()
    {
        trust.Dispose();
        Server.Dispose();
        Git.Dispose();
    }

    private sealed class IdentityHandler((string Name, string Token)[] users) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var index = Array.FindIndex(users, user => user.Token == request.Headers.Authorization!.Parameter);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":" + (index + 1) + ",\"username\":\"" + users[index].Name + "\"}")
            });
        }
    }
}
