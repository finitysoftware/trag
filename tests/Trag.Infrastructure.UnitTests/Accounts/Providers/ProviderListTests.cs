using System.Globalization;
using System.Net;
using Trag.Domain.Catalog;
using Trag.Domain.Errors;
using Trag.Domain.Publish;
using Trag.Infrastructure.Accounts;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Accounts.Providers;

// How much a provider may return, and what is kept from each item. Everything here is bounded: the number of pages,
// the number of owners and the size of a single response.
public sealed class ProviderListTests
{
    private static readonly CancellationToken Ct = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    [Fact]
    public async Task WhenEveryPageIsFull_ShouldStopTheRepositoryListAfterTenPages()
    {
        var pages = new List<int>();
        using var f = new Fixture(request => Page(request, pages, 100));
        var repositories = await f.RepositoriesAsync();
        Assert.Equal(1000, repositories.Count);
        Assert.Equal(Enumerable.Range(1, 10), pages);
        Assert.Equal("group/repo-1-1", repositories[0].FullName);
    }

    [Fact]
    public async Task WhenAPageIsShort_ShouldEndTheListWithoutAskingForTheNextOne()
    {
        var pages = new List<int>();
        using var f = new Fixture(request => Page(request, pages, pages.Count == 0 ? 100 : 50));
        var list = await f.ListAsync();
        Assert.Equal((150, false), (list.Repositories.Count, list.ReachedLimit));
        Assert.Equal([1, 2], pages);
    }

    // The pages decide, not the rows kept: when the tenth page is full the account may hold more, also when some of the
    // thousand read were left out.
    [Fact]
    public async Task WhenTheTenthPageIsFullAndSomeRepositoriesWereLeftOut_ShouldSayTragReadAllThePagesItReads()
    {
        var pages = new List<int>();
        using var f = new Fixture(request => Page(request, pages, 100, httpOnly: 1));
        var list = await f.ListAsync();
        Assert.Equal((990, true), (list.Repositories.Count, list.ReachedLimit));
    }

    [Fact]
    public async Task WhenTheFirstPageIsEmpty_ShouldGiveAnEmptyList()
    {
        var pages = new List<int>();
        using var f = new Fixture(request => Page(request, pages, 0));
        Assert.Empty(await f.RepositoriesAsync());
        Assert.Equal([1], pages);
    }

    [Fact]
    public async Task WhenTheProviderNamesMoreThanTwoHundredOwners_ShouldOfferTheFirstTwoHundred()
    {
        using var f = new Fixture(_ => AccountServiceTests.Reply("[" + string.Join(',', Enumerable.Range(1, 250)
            .Select(i => "{\"id\":" + i + ",\"full_path\":\"group-" + i + "\"}")) + "]"));
        var owners = await f.OwnersAsync();
        Assert.Equal(200, owners.Count);
        Assert.Equal("group-1", owners[0].Display);
        Assert.Equal("group-200", owners[^1].Display);
    }

    // GitLab answers a hundred groups a page; the next page holds the rest, and a short page is the last.
    [Fact]
    public async Task WhenTheAccountHasMoreThanAHundredGroups_ShouldListThemAll()
    {
        var pages = new List<string>();
        using var f = new Fixture(request =>
        {
            var query = request.RequestUri!.Query;
            pages.Add(query);
            var page = query.EndsWith("&page=2", StringComparison.Ordinal) ? 2 : 1;
            return AccountServiceTests.Reply("[" + string.Join(',', Enumerable.Range(1, page == 2 ? 50 : 100)
                .Select(i => "{\"id\":" + (page * 1000 + i) + ",\"full_path\":\"group-" + page + "-" + i + "\"}")) + "]");
        });

        var owners = await f.OwnersAsync();

        Assert.Equal(150, owners.Count);
        Assert.Equal(("group-1-1", "group-2-50"), (owners[0].Display, owners[^1].Display));
        Assert.Equal(["?min_access_level=30&per_page=100&order_by=path&sort=asc&page=1", "?min_access_level=30&per_page=100&order_by=path&sort=asc&page=2"], pages);
    }

    [Fact]
    public async Task WhenTheWebAddressCannotBeUsed_ShouldFallBackToTheCloneAddressWithoutItsSuffix()
    {
        using var f = new Fixture(_ => AccountServiceTests.Reply(
            "[{\"path_with_namespace\":\"group/repo\",\"http_url_to_repo\":\"https://gitlab.example/group/repo.git\",\"web_url\":\"javascript:alert(1)\",\"visibility\":\"public\"}]"));
        var repository = Assert.Single(await f.RepositoriesAsync());
        Assert.Equal("https://gitlab.example/group/repo.git", repository.CloneUrl);
        Assert.Equal("https://gitlab.example/group/repo", repository.WebUrl);
    }

    [Theory]
    [InlineData("private", true)]
    [InlineData("public", false)]
    [InlineData("internal", false)]
    [InlineData("something-new", null)]
    public async Task WhenGitLabNamesAVisibility_ShouldTurnItIntoThePrivateFlagTheUserSees(string visibility, bool? expected)
    {
        using var f = new Fixture(_ => AccountServiceTests.Reply(
            "[{\"path_with_namespace\":\"group/repo\",\"http_url_to_repo\":\"https://gitlab.example/group/repo.git\",\"web_url\":\"https://gitlab.example/group/repo\",\"visibility\":\"" + visibility + "\"}]"));
        Assert.Equal(expected, Assert.Single(await f.RepositoriesAsync()).IsPrivate);
    }

    [Theory]
    [InlineData("http://gitlab.example/group/repo.git")]
    [InlineData("https://user:secret@gitlab.example/group/repo.git")]
    [InlineData("")]
    public async Task WhenARepositoryHasNoUsableHttpsCloneAddress_ShouldLeaveItOut(string clone)
    {
        using var f = new Fixture(_ => AccountServiceTests.Reply(
            "[{\"path_with_namespace\":\"group/repo\",\"http_url_to_repo\":\"" + clone + "\",\"web_url\":\"https://gitlab.example/group/repo\"},"
            + "{\"path_with_namespace\":\"group/kept\",\"http_url_to_repo\":\"https://gitlab.example/group/kept.git\",\"web_url\":\"https://gitlab.example/group/kept\"}]"));
        Assert.Equal("group/kept", Assert.Single(await f.RepositoriesAsync()).FullName);
    }

    // A server whose clone addresses are all http:// lists repositories Trag leaves out, so the list says why it is empty.
    [Fact]
    public async Task WhenNoListedRepositoryHasAUsableHttpsCloneAddress_ShouldSaySoInsteadOfGivingAnEmptyList()
    {
        using var f = new Fixture(_ => AccountServiceTests.Reply(
            "[{\"path_with_namespace\":\"group/one\",\"http_url_to_repo\":\"http://gitlab.example/group/one.git\",\"web_url\":\"https://gitlab.example/group/one\"},"
            + "{\"path_with_namespace\":\"group/two\",\"http_url_to_repo\":\"http://gitlab.example/group/two.git\",\"web_url\":\"https://gitlab.example/group/two\"}]"));
        Assert.Equal("The provider listed repositories, but none has an HTTPS clone address Trag can use.",
            (await Assert.ThrowsAsync<AccountException>(f.RepositoriesAsync)).Message);
    }

    // Such a server with exactly a hundred projects answers a full page and then an empty one.
    [Fact]
    public async Task WhenAFullPageOfHttpOnlyRepositoriesIsFollowedByAnEmptyOne_ShouldStillSayNoneHasAnHttpsCloneAddress()
    {
        var pages = 0;
        using var f = new Fixture(_ => AccountServiceTests.Reply("[" + string.Join(',', Enumerable.Range(1, ++pages == 1 ? 100 : 0).Select(i =>
            "{\"path_with_namespace\":\"group/repo-" + i + "\",\"http_url_to_repo\":\"http://gitlab.example/group/repo-" + i
            + ".git\",\"web_url\":\"https://gitlab.example/group/repo-" + i + "\"}")) + "]"));
        Assert.Equal("The provider listed repositories, but none has an HTTPS clone address Trag can use.",
            (await Assert.ThrowsAsync<AccountException>(f.RepositoriesAsync)).Message);
        Assert.Equal(2, pages);
    }

    [Fact]
    public async Task WhenAResponseIsLargerThanTheLimit_ShouldRefuseItWhileItIsStillBeingRead()
    {
        using var f = new Fixture(_ => new(HttpStatusCode.OK) { Content = new StringContent(new string('x', 4 * 1024 * 1024 + 16)) });
        Assert.Equal("The provider response is too large.", (await Assert.ThrowsAsync<AccountException>(f.RepositoriesAsync)).Message);
    }

    // The first httpOnly repositories of the page have a clone address Trag leaves out.
    private static HttpResponseMessage Page(HttpRequestMessage request, List<int> pages, int count, int httpOnly = 0)
    {
        var query = request.RequestUri!.Query;
        var page = int.Parse(query[(query.LastIndexOf("page=", StringComparison.Ordinal) + 5)..], CultureInfo.InvariantCulture);
        pages.Add(page);
        return AccountServiceTests.Reply("[" + string.Join(',', Enumerable.Range(1, count).Select(i =>
            "{\"path_with_namespace\":\"group/repo-" + page + "-" + i + "\",\"http_url_to_repo\":\"" + (i <= httpOnly ? "http" : "https") + "://gitlab.example/group/repo-" + page + "-" + i
            + ".git\",\"web_url\":\"https://gitlab.example/group/repo-" + page + "-" + i + "\",\"visibility\":\"public\"}")) + "]");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly FakeSecretStore vault = new();
        private readonly AccountService service;
        private readonly Task<string> account;

        public Fixture(Func<HttpRequestMessage, HttpResponseMessage> list)
        {
            service = new(null, vault, new(new AccountServiceTests.Handler((request, _) =>
                request.RequestUri!.AbsolutePath == "/api/v4/user" ? AccountServiceTests.Reply("{\"id\":1,\"username\":\"alice\"}") : list(request))));
            account = SignInAsync();
        }

        private async Task<string> SignInAsync() =>
            (await service.SignInGitLabAsync("https://gitlab.example", "token"u8.ToArray(), true, Ct)).Id;

        public async Task<IReadOnlyList<RemoteRepository>> RepositoriesAsync() => (await ListAsync()).Repositories;

        public async Task<RemoteRepositoryList> ListAsync() =>
            await service.ListRemoteAsync(await account, Ct);

        public async Task<IReadOnlyList<PublishOwner>> OwnersAsync() =>
            await service.OwnersAsync(await account, Ct);

        public void Dispose() => service.Dispose();
    }
}
