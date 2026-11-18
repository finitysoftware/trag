using System.Net;
using System.Text;
using System.Text.Json;
using Trag.Domain.Catalog;
using Trag.Domain.Errors;
using Trag.Infrastructure.Accounts;
using Trag.Infrastructure.Accounts.Providers;
using Trag.Infrastructure.Platform.Secrets;
using Trag.Infrastructure.Platform.Storage;

namespace Trag.Infrastructure.UnitTests.Platform.Storage;

public sealed class RepositoryCatalogStoreTests : IDisposable
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private readonly DateTimeOffset opened = new(2026, 9, 14, 9, 30, 0, TimeSpan.Zero);
    private readonly string folder = Path.Combine(Path.GetTempPath(), "trag-catalog-store-" + Guid.NewGuid().ToString("N"));
    private readonly string path;

    public RepositoryCatalogStoreTests() => path = Path.Combine(folder, "nested", "repositories.json");
    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

    [Fact]
    public async Task WhenNothingWasSaved_ShouldLoadAnEmptyCatalog()
    {
        var store = new RepositoryCatalogStore(path);
        Assert.Empty(await store.LoadAsync(Ct));
    }

    [Fact]
    public async Task WhenEntriesAreSaved_ShouldLoadOnePerPathWithoutBlanksAndLeaveNoTemporaryFile()
    {
        var (_, loaded) = await SavedAsync();
        Assert.Equal(2, loaded.Count);
        Assert.Equal("Trag", loaded[0].Name); Assert.Equal("main", loaded[0].Branch); Assert.Equal(opened, loaded[0].LastOpenedAt); Assert.Equal("https://github.com/finitysoftware/Trag.git", loaded[0].RemoteUrl);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public async Task WhenTheFileIsOversized_ShouldRefuseToLoadIt()
    {
        var (store, _) = await SavedAsync();
        await File.WriteAllBytesAsync(path, new byte[1024 * 1024 + 1], Ct);
        await Assert.ThrowsAsync<IOException>(() => store.LoadAsync(Ct));
    }

    [Fact]
    public async Task WhenTheMemoryStoreSavesWhatTheFileStoreLoaded_ShouldKeepEveryEntry()
    {
        var (store, loaded) = await SavedAsync();
        await File.WriteAllBytesAsync(path, new byte[1024 * 1024 + 1], Ct);
        await Assert.ThrowsAsync<IOException>(() => store.LoadAsync(Ct));
        var memory = new MemoryRepositoryCatalogStore();
        await memory.UpdateAsync(_ => loaded, Ct);
        Assert.Equal(2, (await memory.LoadAsync(Ct)).Count);
    }

    // A null where an entry belongs, which only a hand edit leaves, is dropped as an entry without a path is.
    [Fact]
    public async Task WhenTheFileHoldsANullEntry_ShouldLoadTheOtherEntries()
    {
        var store = await WithANullEntryAsync();
        Assert.Equal(@"D:\Repos\Trag", Assert.Single(await store.LoadAsync(Ct)).Path);
    }

    [Fact]
    public async Task WhenTheFileHoldsANullEntry_ShouldSaveAChangeWithoutIt()
    {
        var store = await WithANullEntryAsync();
        await store.UpdateAsync(entries => [.. entries, new(@"D:\Repos\Atlas", "Atlas", opened)], Ct);
        using var saved = JsonDocument.Parse(await File.ReadAllBytesAsync(path, Ct));
        Assert.Equal([@"D:\Repos\Trag", @"D:\Repos\Atlas"], saved.RootElement.GetProperty("Repositories").EnumerateArray().Select(entry => entry.GetProperty("Path").GetString()));
    }

    [Fact]
    public async Task WhenAGitLabAccountListsItsProjects_ShouldAskWithItsBearerTokenAndSkipUnsafeEntries()
    {
        var requests = new List<HttpRequestMessage>();
        using var vault = new MemorySecretStore();
        using var service = GitLab(vault, requests);
        var account = await service.SignInGitLabAsync("https://gitlab.example", "glpat-token"u8.ToArray(), true, Ct);
        var repositories = (await service.ListRemoteAsync(account.Id, Ct)).Repositories;
        Assert.Equal(2, repositories.Count);
        Assert.Equal("team/app", repositories[0].FullName); Assert.Equal("develop", repositories[0].DefaultBranch); Assert.True(repositories[0].IsPrivate);
        Assert.Equal(new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.Zero), repositories[0].UpdatedAt);
        Assert.Equal("https://gitlab.example/team/public", repositories[1].WebUrl); Assert.False(repositories[1].IsPrivate);
        Assert.Single(requests, request => request.RequestUri!.AbsolutePath == "/api/v4/projects");
    }

    [Fact]
    public async Task WhenAnUnknownAccountListsRepositories_ShouldRefuse()
    {
        var requests = new List<HttpRequestMessage>();
        using var vault = new MemorySecretStore();
        using var service = GitLab(vault, requests);
        var account = await service.SignInGitLabAsync("https://gitlab.example", "glpat-token"u8.ToArray(), true, Ct);
        await service.ListRemoteAsync(account.Id, Ct);
        await Assert.ThrowsAsync<AccountException>(() => service.ListRemoteAsync("account-missing", Ct));
    }

    // Loaded once while empty, then four entries saved - one path twice and a blank one - and loaded back.
    private async Task<(RepositoryCatalogStore Store, IReadOnlyList<RepositoryEntry> Loaded)> SavedAsync()
    {
        var store = new RepositoryCatalogStore(path);
        await store.LoadAsync(Ct);
        var trag = Path.Combine(folder, "Trag");
        await store.UpdateAsync(_ => [new(trag, "Trag", opened, opened, "https://github.com/finitysoftware/Trag.git", "main"),
            new(trag + Path.DirectorySeparatorChar, "", opened), new(Path.Combine(folder, "Atlas"), "Atlas", opened), new("   ", "blank", opened)], Ct);
        return (store, await store.LoadAsync(Ct));
    }

    private async Task<RepositoryCatalogStore> WithANullEntryAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """{"Repositories":[null,{"Path":"D:\\Repos\\Trag","Name":"Trag","AddedAt":"2026-09-14T09:30:00+00:00"}]}""", Ct);
        return new RepositoryCatalogStore(path);
    }

    // A GitLab that answers the user and three projects: a private one, one over plain HTTP and a public one.
    private static AccountService GitLab(MemorySecretStore vault, List<HttpRequestMessage> requests) => new(null, vault, new ProviderClient(new Handler(request =>
    {
        requests.Add(request);
        if (request.RequestUri!.AbsolutePath.EndsWith("/user", StringComparison.Ordinal)) return Json("{\"id\":1,\"username\":\"alice\"}");
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme); Assert.Equal("glpat-token", request.Headers.Authorization.Parameter);
        Assert.Contains("membership=true", request.RequestUri.Query);
        return Json("[{\"path_with_namespace\":\"team/app\",\"http_url_to_repo\":\"https://gitlab.example/team/app.git\",\"web_url\":\"https://gitlab.example/team/app\"," +
            "\"default_branch\":\"develop\",\"visibility\":\"private\",\"last_activity_at\":\"2026-09-10T10:00:00Z\"}," +
            "{\"path_with_namespace\":\"team/insecure\",\"http_url_to_repo\":\"http://gitlab.example/team/insecure.git\"}," +
            "{\"path_with_namespace\":\"team/public\",\"http_url_to_repo\":\"https://gitlab.example/team/public.git\",\"visibility\":\"public\"}]");
    })));

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
    }
}
