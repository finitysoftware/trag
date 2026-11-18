using System.Net;
using System.Text;
using Trag.Domain.Accounts;
using Trag.Domain.Errors;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Accounts;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Accounts.Providers;

public sealed class PublishProviderTests
{
    private static readonly CancellationToken Ct = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    [Fact]
    public async Task WhenGitLabListsGroups_ShouldOfferOnlyTheUsableGroupsTheUserMayCreateIn()
    {
        var requests = new List<(string Method, string Path, string Body)>();
        using var service = GitLab(requests);
        var account = await service.SignInGitLabAsync("https://gitlab.com", "token"u8.ToArray(), true, Ct);
        var owners = await service.OwnersAsync(account.Id, Ct);
        var group = Assert.Single(owners); Assert.Equal("12", group.Id); Assert.Equal("team/sub", group.Display); Assert.False(group.IsUser);
        Assert.Contains(requests, item => item.Path.StartsWith("/api/v4/groups?min_access_level=30", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenAProjectIsCreatedInAGitLabGroup_ShouldPostItIntoThatNamespaceWithoutAReadme()
    {
        var requests = new List<(string Method, string Path, string Body)>();
        using var service = GitLab(requests);
        var (account, groupId) = await GitLabGroupAsync(service);
        var created = await service.CreateRepositoryAsync(account.Id, groupId, "trag", true, Ct);
        Assert.Equal("team/sub/trag", created.FullName); Assert.Equal("https://gitlab.com/team/sub/trag.git", created.CloneUrl); Assert.True(created.IsPrivate);
        var post = Assert.Single(requests, item => item.Method == "POST");
        Assert.Contains("\"name\":\"trag\"", post.Body); Assert.Contains("\"path\":\"trag\"", post.Body); Assert.Contains("\"visibility\":\"private\"", post.Body);
        Assert.Contains("\"namespace_id\":12", post.Body); Assert.Contains("\"initialize_with_readme\":false", post.Body); Assert.DoesNotContain("auto_init", post.Body);
    }

    [Fact]
    public async Task WhenTheGitLabOwnerIsNotANumber_ShouldRefuseItBeforeSending()
    {
        var requests = new List<(string Method, string Path, string Body)>();
        using var service = GitLab(requests);
        var account = await CreatedInTheGitLabGroupAsync(service);
        await Assert.ThrowsAsync<AccountException>(() => service.CreateRepositoryAsync(account.Id, "not-a-number", "trag", true, Ct));
        Assert.Single(requests, item => item.Method == "POST");
    }

    [Fact]
    public async Task WhenTheGitLabProjectNameIsInvalid_ShouldExplainTheRulesBeforeSending()
    {
        var requests = new List<(string Method, string Path, string Body)>();
        using var service = GitLab(requests);
        var account = await CreatedInTheGitLabGroupAsync(service);
        await Assert.ThrowsAsync<AccountException>(() => service.CreateRepositoryAsync(account.Id, "not-a-number", "trag", true, Ct));
        var invalid = await Assert.ThrowsAsync<AccountException>(() => service.CreateRepositoryAsync(account.Id, null, "bad name", true, Ct));
        Assert.Equal(PublishNames.Help, invalid.Message);
        Assert.Single(requests, item => item.Method == "POST");
    }

    [Fact]
    public async Task WhenSigningInToGitHub_ShouldAskForTheScopesPublishingNeeds()
    {
        var github = new GitHub(); var requests = github.Requests;
        using var service = github.Service();
        await service.SignInGitHubAsync(true, new Sink(), Ct);
        Assert.Contains(requests, item => item.Path == "/login/device/code" && item.Body.Contains("scope=repo+workflow+admin%3Aorg", StringComparison.Ordinal) && item.Body.Contains("+user+", StringComparison.Ordinal));
    }

    // GitHub answers a hundred organizations a page; the next page holds the rest.
    [Fact]
    public async Task WhenTheAccountHasMoreThanAHundredOrganizations_ShouldListThemAll()
    {
        var github = new GitHub { Organizations = query => "[" + string.Join(',', Enumerable.Range(1, query.EndsWith("&page=2", StringComparison.Ordinal) ? 50 : 100)
            .Select(i => "{\"login\":\"org-" + query[^1] + "-" + i + "\",\"id\":" + i + "}")) + "]" };
        using var service = github.Service();
        var account = await service.SignInGitHubAsync(true, new Sink(), Ct);

        var owners = await service.OwnersAsync(account.Id, Ct);

        Assert.Equal(150, owners.Count);
        Assert.Equal(("org-1-1", "org-2-50"), (owners[0].Id, owners[^1].Id));
        Assert.Equal(["/user/orgs?per_page=100&page=1", "/user/orgs?per_page=100&page=2"], github.Requests.Select(item => item.Path).Where(path => path.StartsWith("/user/orgs", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task WhenGitHubListsOrganizations_ShouldOfferOnlyThoseWithAUsableName()
    {
        var github = new GitHub();
        using var service = github.Service();
        var account = await service.SignInGitHubAsync(true, new Sink(), Ct);
        var owners = await service.OwnersAsync(account.Id, Ct);
        var organization = Assert.Single(owners); Assert.Equal("acme", organization.Id);
    }

    [Fact]
    public async Task WhenARepositoryIsCreatedUnderTheGitHubUser_ShouldPostItToTheUsersRepositoriesWithoutAReadme()
    {
        var github = new GitHub(); var requests = github.Requests;
        using var service = github.Service();
        var (account, _) = await GitHubOrganizationAsync(service);
        var mine = await service.CreateRepositoryAsync(account.Id, null, "trag", false, Ct);
        Assert.Equal("alice/trag", mine.FullName); Assert.Equal("https://github.com/acme/trag", mine.WebUrl);
        var post = requests.Last(item => item.Method == "POST");
        Assert.Equal("/user/repos", post.Path); Assert.Contains("\"private\":false", post.Body); Assert.Contains("\"auto_init\":false", post.Body);
        Assert.DoesNotContain("visibility", post.Body); Assert.DoesNotContain("namespace_id", post.Body);
    }

    [Fact]
    public async Task WhenARepositoryIsCreatedUnderAGitHubOrganization_ShouldPostItToTheOrganizationsRepositories()
    {
        var github = new GitHub(); var requests = github.Requests;
        using var service = github.Service();
        var (account, organizationId) = await CreatedUnderTheGitHubUserAsync(service);
        var theirs = await service.CreateRepositoryAsync(account.Id, organizationId, "trag", true, Ct);
        Assert.Equal("acme/trag", theirs.FullName);
        var post = requests.Last(item => item.Method == "POST"); Assert.Equal("/orgs/acme/repos", post.Path); Assert.Contains("\"private\":true", post.Body);
    }

    [Fact]
    public async Task WhenGitHubSaysTheRepositoryExists_ShouldSaySo()
    {
        var github = new GitHub();
        using var service = github.Service();
        var account = await CreatedUnderTheGitHubUserAndOrganizationAsync(service);
        github.Status = HttpStatusCode.UnprocessableEntity;
        Assert.Contains("already exist", (await Assert.ThrowsAsync<AccountException>(() => service.CreateRepositoryAsync(account.Id, null, "trag", true, Ct))).Message);
    }

    [Fact]
    public async Task WhenGitHubDeniesTheCreation_ShouldSaySo()
    {
        var github = new GitHub();
        using var service = github.Service();
        var account = await CreatedUnderTheGitHubUserAndOrganizationAsync(service);
        github.Status = HttpStatusCode.UnprocessableEntity;
        Assert.Contains("already exist", (await Assert.ThrowsAsync<AccountException>(() => service.CreateRepositoryAsync(account.Id, null, "trag", true, Ct))).Message);
        github.Status = HttpStatusCode.Forbidden;
        Assert.Contains("denied", (await Assert.ThrowsAsync<AccountException>(() => service.CreateRepositoryAsync(account.Id, null, "trag", true, Ct))).Message);
    }

    // A used-up rate limit is a wait, not a missing permission: the organization list and a creation both say to wait.
    [Theory]
    [InlineData("X-RateLimit-Remaining", "0")]
    [InlineData("Retry-After", "60")]
    public async Task WhenGitHubAnswers403ForARateLimit_ShouldSayRateLimited(string header, string value)
    {
        var github = new GitHub();
        using var service = github.Service();
        var account = await CreatedUnderTheGitHubUserAndOrganizationAsync(service);
        github.RateLimit = (header, value);

        var owners = await Assert.ThrowsAsync<AccountException>(() => service.OwnersAsync(account.Id, Ct));
        var creation = await Assert.ThrowsAsync<AccountException>(() => service.CreateRepositoryAsync(account.Id, null, "trag", true, Ct));

        Assert.Equal((ProviderText.RateLimited, ProviderText.RateLimited), (owners.Message, creation.Message));
    }

    // Statuses without a sentence of their own: a redirect, which is never followed because it would take the token
    // elsewhere, a missing page and a server error; and a token that is no longer valid. A missing page on creation is the
    // owner the account cannot reach.
    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently, ProviderText.Failed, ProviderText.Failed, ProviderText.Failed)]
    [InlineData(HttpStatusCode.NotFound, ProviderText.Failed, ProviderText.Failed, ProviderText.OwnerNotFound)]
    [InlineData(HttpStatusCode.InternalServerError, ProviderText.Failed, ProviderText.Failed, ProviderText.Failed)]
    [InlineData(HttpStatusCode.Unauthorized, ProviderText.TokenRevoked, ProviderText.TokenRevoked, ProviderText.TokenRevoked)]
    public async Task WhenGitHubAnswersAStatusWithoutItsOwnSentence_ShouldSayWhatTheUserCanDo(HttpStatusCode status, string list, string owners, string creation)
    {
        var github = new GitHub();
        using var service = github.Service();
        var (account, _) = await GitHubOrganizationAsync(service);
        github.Answer = status;

        var listed = await Assert.ThrowsAsync<AccountException>(() => service.ListRemoteAsync(account.Id, Ct));
        var owned = await Assert.ThrowsAsync<AccountException>(() => service.OwnersAsync(account.Id, Ct));
        var created = await Assert.ThrowsAsync<AccountException>(() => service.CreateRepositoryAsync(account.Id, null, "trag", true, Ct));

        Assert.Equal((list, owners, creation), (listed.Message, owned.Message, created.Message));
    }

    [Fact]
    public async Task WhenTheGitHubOwnerIsNotAName_ShouldRefuseItBeforeSendingAnything()
    {
        var github = new GitHub(); var requests = github.Requests;
        using var service = github.Service();
        var account = await CreatedUnderTheGitHubUserAndOrganizationAsync(service);
        github.Status = HttpStatusCode.UnprocessableEntity;
        Assert.Contains("already exist", (await Assert.ThrowsAsync<AccountException>(() => service.CreateRepositoryAsync(account.Id, null, "trag", true, Ct))).Message);
        github.Status = HttpStatusCode.Forbidden;
        Assert.Contains("denied", (await Assert.ThrowsAsync<AccountException>(() => service.CreateRepositoryAsync(account.Id, null, "trag", true, Ct))).Message);
        await Assert.ThrowsAsync<AccountException>(() => service.CreateRepositoryAsync(account.Id, "../admin", "trag", true, Ct));
        Assert.DoesNotContain(requests, item => item.Path.Contains("admin", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenGitLabRefusesTheOwnerListForAMissingScope_ShouldNameTheScopeWithoutRepeatingTheBody()
    {
        using var service = WithoutTheApiScope();
        var account = await service.SignInGitLabAsync("https://gitlab.com", "secret-token"u8.ToArray(), true, Ct);
        var error = await Assert.ThrowsAsync<AccountException>(() => service.OwnersAsync(account.Id, Ct));
        Assert.Contains("api scope", error.Message); Assert.DoesNotContain("secret-token", error.Message);
    }

    [Fact]
    public async Task WhenGitLabRefusesTheCreationForAMissingScope_ShouldNameTheScope()
    {
        using var service = WithoutTheApiScope();
        var account = await service.SignInGitLabAsync("https://gitlab.com", "secret-token"u8.ToArray(), true, Ct);
        await Assert.ThrowsAsync<AccountException>(() => service.OwnersAsync(account.Id, Ct));
        Assert.Contains("api scope", (await Assert.ThrowsAsync<AccountException>(() => service.CreateRepositoryAsync(account.Id, null, "trag", true, Ct))).Message);
    }

    [Theory]
    [InlineData("Trag", "Trag")]
    [InlineData("my repo!", "my-repo")]
    [InlineData("..", "repository")]
    [InlineData("service.git", "service")]
    public void WhenANameIsSuggestedForAFolder_ShouldBeValidForBothProviders(string folder, string expected)
    {
        Assert.Equal(expected, PublishNames.Suggest(folder));
        Assert.True(PublishNames.IsValid(expected));
        Assert.NotEmpty(PublishNames.Error(".hidden")); Assert.NotEmpty(PublishNames.Error("ends.git")); Assert.NotEmpty(PublishNames.Error("has space"));
        Assert.Empty(PublishNames.Error("ok_name-1.x")); Assert.Empty(PublishNames.Error(""));
    }

    // GitLab with one usable group among two that are not; a project created in it comes back private.
    private static AccountService GitLab(List<(string Method, string Path, string Body)> requests)
    {
        var vault = new FakeSecretStore();
        return new AccountService(null, vault, new(new AccountServiceTests.Handler((request, body) =>
        {
            requests.Add((request.Method.Method, request.RequestUri!.PathAndQuery, body));
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v4/user") return AccountServiceTests.Reply("{\"id\":1,\"username\":\"alice\"}");
            if (path == "/api/v4/groups") return AccountServiceTests.Reply("[{\"id\":12,\"full_path\":\"team/sub\",\"name\":\"Sub\"},{\"id\":13,\"full_path\":\"bad\\u0001path\",\"name\":\"x\"},{\"full_path\":\"no-id\"}]");
            if (path == "/api/v4/projects" && request.Method == HttpMethod.Post)
                return Json(HttpStatusCode.Created, "{\"path_with_namespace\":\"team/sub/trag\",\"http_url_to_repo\":\"https://gitlab.com/team/sub/trag.git\",\"web_url\":\"https://gitlab.com/team/sub/trag\",\"default_branch\":null,\"visibility\":\"private\"}");
            throw new InvalidOperationException(path);
        })));
    }

    // Signed in for the session, then the groups were listed.
    private static async Task<(AccountInfo Account, string GroupId)> GitLabGroupAsync(AccountService service)
    {
        var account = await service.SignInGitLabAsync("https://gitlab.com", "token"u8.ToArray(), true, Ct);
        var owners = await service.OwnersAsync(account.Id, Ct);
        return (account, Assert.Single(owners).Id);
    }

    // ... and a private project was created in the group.
    private static async Task<AccountInfo> CreatedInTheGitLabGroupAsync(AccountService service)
    {
        var (account, groupId) = await GitLabGroupAsync(service);
        await service.CreateRepositoryAsync(account.Id, groupId, "trag", true, Ct);
        return account;
    }

    // Signed in for the session, then the organizations were listed.
    private static async Task<(AccountInfo Account, string OrganizationId)> GitHubOrganizationAsync(AccountService service)
    {
        var account = await service.SignInGitHubAsync(true, new Sink(), Ct);
        var owners = await service.OwnersAsync(account.Id, Ct);
        return (account, Assert.Single(owners).Id);
    }

    // ... and a public repository was created under the user.
    private static async Task<(AccountInfo Account, string OrganizationId)> CreatedUnderTheGitHubUserAsync(AccountService service)
    {
        var (account, organizationId) = await GitHubOrganizationAsync(service);
        await service.CreateRepositoryAsync(account.Id, null, "trag", false, Ct);
        return (account, organizationId);
    }

    // ... and a private one under the organization.
    private static async Task<AccountInfo> CreatedUnderTheGitHubUserAndOrganizationAsync(AccountService service)
    {
        var (account, organizationId) = await CreatedUnderTheGitHubUserAsync(service);
        await service.CreateRepositoryAsync(account.Id, organizationId, "trag", true, Ct);
        return account;
    }

    // GitLab signs in, then refuses everything else for a missing scope and echoes the token in its body.
    private static AccountService WithoutTheApiScope()
    {
        var vault = new FakeSecretStore();
        return new AccountService(null, vault, new(new AccountServiceTests.Handler((request, _) =>
            request.RequestUri!.AbsolutePath == "/api/v4/user" ? AccountServiceTests.Reply("{\"id\":1,\"username\":\"alice\"}") : new(HttpStatusCode.Forbidden) { Content = new StringContent("{\"message\":\"insufficient_scope secret-token\"}") })));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class Sink : IProgress<DeviceLoginProgress> { public void Report(DeviceLoginProgress value) { } }
    // GitHub with one usable organization among two; a creation is answered with Status.
    private sealed class GitHub
    {
        public List<(string Method, string Path, string Body)> Requests { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.Created;
        // Every answer after sign-in is a 403 with this header, as GitHub's rate limits answer: no requests left for the
        // primary one, a time to wait for the secondary one.
        public (string Name, string Value)? RateLimit { get; set; }
        // Every answer after sign-in, when set.
        public HttpStatusCode? Answer { get; set; }
        // The organizations' answer to a query, when set.
        public Func<string, string>? Organizations { get; set; }
        public AccountService Service()
        {
            var vault = new FakeSecretStore();
            return new AccountService(null, vault, new(new AccountServiceTests.Handler((request, body) =>
            {
                Requests.Add((request.Method.Method, request.RequestUri!.PathAndQuery, body));
                var path = request.RequestUri!.AbsolutePath;
                if (path == "/login/device/code") return AccountServiceTests.Reply("{\"device_code\":\"device\",\"user_code\":\"ABCD-1234\",\"verification_uri\":\"https://github.com/login/device\",\"expires_in\":900,\"interval\":5}");
                if (path == "/login/oauth/access_token") return AccountServiceTests.Reply("{\"access_token\":\"gh-token\",\"token_type\":\"bearer\",\"scope\":\"repo,read:user,read:org\"}");
                if (path == "/user") return AccountServiceTests.Reply("{\"id\":7,\"login\":\"alice\"}");
                if (RateLimit is { } header)
                {
                    var limited = Json(HttpStatusCode.Forbidden, "{\"message\":\"API rate limit exceeded\"}");
                    limited.Headers.Add(header.Name, header.Value);
                    return limited;
                }
                if (Answer is { } answer) return Json(answer, "{\"message\":\"answered\"}");
                if (path == "/user/orgs") return AccountServiceTests.Reply(Organizations?.Invoke(request.RequestUri!.Query) ?? "[{\"login\":\"acme\",\"id\":1},{\"login\":\"bad login\",\"id\":2}]");
                if (path is "/user/repos" or "/orgs/acme/repos" && request.Method == HttpMethod.Post)
                    return Json(Status, "{\"full_name\":\"" + (path == "/user/repos" ? "alice" : "acme") + "/trag\",\"clone_url\":\"https://github.com/acme/trag.git\",\"html_url\":\"https://github.com/acme/trag\",\"default_branch\":\"main\",\"private\":false}");
                throw new InvalidOperationException(path);
            }), new FastClock()));
        }
    }
    private sealed class FastClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { now += dueTime; return new Timer(callback, state, TimeSpan.Zero, Timeout.InfiniteTimeSpan); }
    }
}
