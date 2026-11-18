using System.Text;
using Trag.Domain.Catalog;
using Trag.Domain.Publish;
using Trag.Domain.Writes;
using Trag.Infrastructure.Accounts;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Write;

namespace Trag.IntegrationTests.Accounts;

// Every failure of a sync, a clone, a publish and a submodule update with a token in play, from each place a token can
// come from: the server refuses it and echoes it back in plain text and in its Basic form, which Git prints as the
// remote's message, and curl's verbose trace prints the headers Git sent, unredacted. Neither the message the status bar
// shows nor the details carry it, and Trag writes it into no file it keeps. The trace is set for the whole process, so
// the class runs alone.
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class CredentialLeakTests
{
    private readonly string token = "leak-" + Guid.NewGuid().ToString("N");

    // Git's own credentials, written into the remote's address by the user.
    [Theory]
    [InlineData("fetch")]
    [InlineData("pull")]
    [InlineData("push")]
    [InlineData("clone")]
    [InlineData("publish")]
    public async Task WhenGitsOwnTokenInTheRemoteAddressIsRefused_ShouldShowItNowhere(string operation)
    {
        using var f = await RefusingServerAsync();
        using var verbose = new CurlTrace();
        var address = f.Server.Url("team/project.git").Replace("https://", "https://alice:" + token + "@", StringComparison.Ordinal);

        var shown = await RunAsync(f, operation, address, null, null);

        AssertHidden(shown);
        Assert.Contains("alice:" + token, f.Server.Credentials);
    }

    // The account's token, which only Trag's askpass helper hands to Git.
    [Theory]
    [InlineData("fetch")]
    [InlineData("pull")]
    [InlineData("push")]
    [InlineData("clone")]
    [InlineData("publish")]
    [InlineData("submodules")]
    public async Task WhenTheAccountsTokenIsRefused_ShouldShowItNowhereNorWriteItIntoAFileTragKeeps(string operation)
    {
        using var f = await RefusingServerAsync();
        using var verbose = new CurlTrace();
        using var accounts = await f.SignInAsync(("alice", token));
        var alice = (await accounts.AccountsAsync(f.Token)).Single();

        var shown = await RunAsync(f, operation, f.Server.Url("team/project.git"), accounts, alice.Id);

        AssertHidden(shown);
        Assert.Contains("alice:" + token, f.Server.Credentials);
        foreach (var kept in new[] { Path.Combine(f.Container, "accounts.json"), Path.Combine(f.Git.Root, ".git", "config") })
            Assert.DoesNotContain(token, await File.ReadAllTextAsync(kept));
    }

    // Git's own credentials as a header the user configured for every request: a clone has no configuration of its own
    // to carry it before it starts. Git stops to ask for a user name before it would print the server's echo of the
    // refused header, so the token reaches Git's output through curl's trace.
    [Theory]
    [InlineData("fetch")]
    [InlineData("pull")]
    [InlineData("push")]
    [InlineData("publish")]
    public async Task WhenGitsOwnTokenInAnExtraHeaderIsRefused_ShouldShowItNowhere(string operation)
    {
        using var f = await RefusingServerAsync();
        using var verbose = new CurlTrace();
        await f.Git.GitAsync("config", "http.extraHeader", "Authorization: Bearer " + token);

        var shown = await RunAsync(f, operation, f.Server.Url("team/project.git"), null, null);

        AssertHidden(shown);
        Assert.Contains("Bearer " + token, Encoding.UTF8.GetString((await f.Git.RunAsync("fetch", "origin")).StandardError));
    }

    // Git's own credentials, written into a submodule's address in .gitmodules: the repository itself is cloned with the
    // ones the server accepts, so only the submodule fails, and the clone still succeeds.
    [Theory]
    [InlineData("clone")]
    [InlineData("submodules")]
    public async Task WhenGitsOwnTokenInASubmoduleAddressIsRefused_ShouldShowItNowhere(string operation)
    {
        var accepted = "accepted-" + Guid.NewGuid().ToString("N");
        using var f = await HttpsRemoteFixture.CreateAsync("alice", accepted, TimeSpan.FromMinutes(2));
        await f.PublishAsync(Path.Combine(f.Container, "lib"), "lib");
        await f.PublishAsync(f.Git.Root, "project", "lib", f.Server.Url("team/lib.git").Replace("https://", "https://alice:" + token + "@", StringComparison.Ordinal));
        using var verbose = new CurlTrace();

        Shown shown;
        if (operation == "clone")
        {
            var address = f.Server.Url("team/project.git").Replace("https://", "https://alice:" + accepted + "@", StringComparison.Ordinal);
            var cloned = await f.Catalog().CloneAsync(new CloneRequest(address, Path.Combine(f.Container, "clone"), Submodules: true), f.Token);
            shown = new(cloned.Message, cloned.Details);
        }
        else
        {
            await f.Git.GitAsync("submodule", "deinit", "--force", "--", "lib");
            Directory.Move(Path.Combine(f.Git.Root, ".git", "modules", "lib"), Path.Combine(f.Container, "lib-module"));
            var result = await new GitWriteRouter(f.Git.Runner, f.Sync(), new CliRepositoryReader(f.Git.Runner)).ExecuteAsync(new SubmoduleUpdateRequest(await f.Git.ReadAsync()), f.Token);
            Assert.NotEqual(GitWriteOutcome.Succeeded, result.Outcome);
            shown = new(result.Message, result.Details);
        }

        AssertHidden(shown);
        Assert.Contains("alice:" + token, f.Server.Credentials);
    }

    private static async Task<HttpsRemoteFixture> RefusingServerAsync()
    {
        var f = await HttpsRemoteFixture.CreateAsync("alice", "accepted-" + Guid.NewGuid().ToString("N"), TimeSpan.FromMinutes(2));
        try
        {
            await f.PublishWithNestedSubmoduleAsync();
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    // What the user is shown for the operation on the fixture's repository with the address as its origin.
    private static async Task<Shown> RunAsync(HttpsRemoteFixture f, string operation, string address, AccountService? accounts, string? accountId)
    {
        if (operation == "clone")
        {
            var cloned = await f.Catalog(accounts).CloneAsync(new CloneRequest(address, Path.Combine(f.Container, "clone"), accountId, Submodules: true), f.Token);
            Assert.False(cloned.Succeeded);
            return new(cloned.Message, cloned.Details);
        }
        var sync = f.Sync(accounts);
        GitWriteResult result;
        if (operation == "publish")
            result = await sync.ExecuteAsync(new PublishRequest(await f.Git.ReadAsync(), new(PublishKind.AddRemote, "origin", "main", true, RemoteUrl: address, AccountId: accountId)), f.Token);
        else
        {
            await f.Git.GitAsync("remote", "add", "origin", address);
            await f.Git.GitAsync("update-ref", "refs/remotes/origin/main", "HEAD");
            await f.Git.GitAsync("branch", "--set-upstream-to=origin/main");
            await f.Git.WriteAsync("b.txt", "b\n");
            await f.Git.CommitAsync("Second", "b.txt");
            if (operation == "submodules")
            {
                // The submodule as a new clone would find it: not there yet, so updating it has to clone it.
                await f.Git.GitAsync("submodule", "deinit", "--force", "--", "lib");
                Directory.Move(Path.Combine(f.Git.Root, ".git", "modules", "lib"), Path.Combine(f.Container, "lib-module"));
                result = await new GitWriteRouter(f.Git.Runner, sync, new CliRepositoryReader(f.Git.Runner)).ExecuteAsync(new SubmoduleUpdateRequest(await f.Git.ReadAsync()), f.Token);
            }
            else result = await f.ExecuteAsync(sync, operation switch { "fetch" => SyncAction.Fetch, "pull" => SyncAction.Pull, _ => SyncAction.Push });
        }
        Assert.NotEqual(GitWriteOutcome.Succeeded, result.Outcome);
        return new(result.Message, result.Details);
    }

    private void AssertHidden(Shown shown)
    {
        foreach (var form in new[] { token, Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:" + token)) })
        {
            Assert.DoesNotContain(form, shown.Message);
            Assert.DoesNotContain(form, shown.Details);
        }
    }

    private sealed record Shown(string Message, string Details);

    // curl's trace of every request Git makes, with the headers as they were sent.
    private sealed class CurlTrace : IDisposable
    {
        public CurlTrace()
        {
            Environment.SetEnvironmentVariable("GIT_CURL_VERBOSE", "1");
            Environment.SetEnvironmentVariable("GIT_TRACE_REDACT", "0");
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("GIT_CURL_VERBOSE", null);
            Environment.SetEnvironmentVariable("GIT_TRACE_REDACT", null);
        }
    }
}
