using Trag.Domain.Catalog;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Write;

namespace Trag.IntegrationTests.Accounts;

// Real Git over HTTPS asks Trag's own executable, as its askpass helper, for the chosen account's token: the server
// answers 401 until a request carries the one it accepts, and records what every request carried. Git trusts the
// server's certificate through settings given to the whole process, so the class runs alone. A fifth prompt for one
// repository and a prompt minutes into an operation are the broker's own rules, which CredentialBrokerTests and
// CredentialBrokerLifetimeTests hold: Git asks once per repository and process, so no Git run reaches them.
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class CredentialBrokerEndToEndTests
{
    private readonly string aliceToken = "alice-" + Guid.NewGuid().ToString("N");
    private readonly string bobToken = "bob-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task WhenTheRepositoryChoseOneOfTwoAccounts_ShouldFetchAndPushWithThatAccountsTokenOnly()
    {
        using var f = await HttpsRemoteFixture.CreateAsync("bob", bobToken, TimeSpan.FromMinutes(2));
        await f.PublishAsync(f.Git.Root, "project");
        using var accounts = await f.SignInAsync(("alice", aliceToken), ("bob", bobToken));
        await f.Git.GitAsync("remote", "add", "origin", f.Server.Url("team/project.git"));
        var sync = f.Sync(accounts);
        var bob = (await accounts.AccountsAsync(f.Token)).Single(account => account.UserName == "bob");
        Assert.Equal(GitWriteOutcome.Succeeded, (await f.ExecuteAsync(sync, SyncAction.SelectAccount, bob.Id)).Outcome);

        var fetched = await f.ExecuteAsync(sync, SyncAction.Fetch);
        Assert.True(fetched.Outcome == GitWriteOutcome.Succeeded, fetched.Message + fetched.Details);
        await f.Git.GitAsync("branch", "--set-upstream-to=origin/main");
        await f.Git.WriteAsync("b.txt", "b\n");
        var head = await f.Git.CommitAsync("Second", "b.txt");
        var pushed = await f.ExecuteAsync(sync, SyncAction.Push);

        Assert.True(pushed.Outcome == GitWriteOutcome.Succeeded, pushed.Message + pushed.Details);
        Assert.Equal(head, await f.Git.GitAtAsync(f.Bare("project"), "rev-parse", "main"));
        Assert.Equal(["bob:" + bobToken], f.Server.Credentials.OfType<string>().Distinct());
    }

    // The repository's submodule and the submodule's own live on the same server: a clone with its submodules, or a
    // clone followed by Update submodules, asks for each of the three with the account's token.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenARepositoryWithANestedSubmoduleOnTheSameServerIsCloned_ShouldBringEveryRepositoryWithTheAccountsToken(bool submodulesWithTheClone)
    {
        using var f = await HttpsRemoteFixture.CreateAsync("alice", aliceToken, TimeSpan.FromMinutes(3));
        await f.PublishWithNestedSubmoduleAsync();
        using var accounts = await f.SignInAsync(("alice", aliceToken));
        var alice = (await accounts.AccountsAsync(f.Token)).Single();
        var target = Path.Combine(f.Container, "clone");

        var cloned = await f.Catalog(accounts).CloneAsync(new CloneRequest(f.Server.Url("team/project.git"), target, alice.Id, Submodules: submodulesWithTheClone), f.Token);
        Assert.True(cloned.Succeeded, cloned.Message + cloned.Details);
        if (!submodulesWithTheClone)
        {
            var updated = await new GitWriteRouter(f.Git.Runner, f.Sync(accounts), new CliRepositoryReader(f.Git.Runner))
                .ExecuteAsync(new SubmoduleUpdateRequest(await f.Git.ReadAsync(target)), f.Token);
            Assert.True(updated.Outcome == GitWriteOutcome.Succeeded, updated.Message + updated.Details);
        }

        Assert.Equal("n\n", await File.ReadAllTextAsync(Path.Combine(target, "lib", "nested", "n.txt")));
        Assert.Equal(["alice:" + aliceToken], f.Server.Credentials.OfType<string>().Distinct());
    }

    // The account's token reaches the server and is refused: the user reads that authentication failed, for a fetch and
    // for a clone.
    [Fact]
    public async Task WhenTheServerRefusesTheAccountsToken_ShouldSayAuthenticationFailed()
    {
        using var f = await HttpsRemoteFixture.CreateAsync("alice", "alice-" + Guid.NewGuid().ToString("N"), TimeSpan.FromMinutes(2));
        await f.PublishAsync(f.Git.Root, "project");
        using var accounts = await f.SignInAsync(("alice", aliceToken));
        var alice = (await accounts.AccountsAsync(f.Token)).Single();
        await f.Git.GitAsync("remote", "add", "origin", f.Server.Url("team/project.git"));

        var fetched = await f.ExecuteAsync(f.Sync(accounts), SyncAction.Fetch);
        var cloned = await f.Catalog(accounts).CloneAsync(new CloneRequest(f.Server.Url("team/project.git"), Path.Combine(f.Container, "clone"), alice.Id), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, TransportText.AuthenticationFailed), (fetched.Outcome, fetched.Message));
        Assert.Equal((false, CloneText.AuthenticationFailed), (cloned.Succeeded, cloned.Message));
        Assert.Equal(["alice:" + aliceToken], f.Server.Credentials.OfType<string>().Distinct());
    }
}
