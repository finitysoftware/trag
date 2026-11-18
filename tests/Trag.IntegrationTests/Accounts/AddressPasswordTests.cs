using System.Text;
using Trag.Domain.Catalog;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;

namespace Trag.IntegrationTests.Accounts;

// A password the user wrote into a remote's or a clone's address can be as short as a letter or read like a branch name,
// so it may stand anywhere in what Git prints. Trag hides it only from the details it shows and reads Git's output as Git
// wrote it.
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class AddressPasswordTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("main")]
    public async Task WhenTheRemoteAddressCarriesAPasswordGitPrintsAnyway_ShouldPushAsWithAnyOther(string password)
    {
        using var f = await HttpsRemoteFixture.CreateAsync("alice", password, TimeSpan.FromMinutes(2));
        await f.PublishAsync(f.Git.Root, "project");
        await f.Git.GitAsync("remote", "add", "origin", Address(f, password));
        await f.Git.GitAsync("update-ref", "refs/remotes/origin/main", "HEAD");
        await f.Git.GitAsync("branch", "--set-upstream-to=origin/main");
        await f.Git.WriteAsync("b.txt", "b\n");
        await f.Git.CommitAsync("Second", "b.txt");

        var result = await f.ExecuteAsync(f.Sync(), SyncAction.Push);

        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
    }

    [Fact]
    public async Task WhenAOneLetterPasswordInTheRemoteAddressIsRefused_ShouldSayAuthenticationFailedAndShowItNowhere()
    {
        using var f = await RefusingServerAsync();
        await f.Git.GitAsync("remote", "add", "origin", Address(f, "a"));

        var result = await f.ExecuteAsync(f.Sync(), SyncAction.Fetch);

        Assert.Equal(TransportText.AuthenticationFailed, result.Message);
        Assert.True(result.RefusedCredentials);
        AssertHidden(result.Details);
    }

    [Fact]
    public async Task WhenAOneLetterPasswordInTheCloneAddressIsRefused_ShouldSayAuthenticationFailedAndShowItNowhere()
    {
        using var f = await RefusingServerAsync();

        var cloned = await f.Catalog().CloneAsync(new CloneRequest(Address(f, "a"), Path.Combine(f.Container, "clone")), f.Token);

        Assert.Equal(CloneText.AuthenticationFailed, cloned.Message);
        AssertHidden(cloned.Details);
    }

    private static string Address(HttpsRemoteFixture f, string password) =>
        f.Server.Url("team/project.git").Replace("https://", "https://alice:" + password + "@", StringComparison.Ordinal);

    private static async Task<HttpsRemoteFixture> RefusingServerAsync()
    {
        var f = await HttpsRemoteFixture.CreateAsync("alice", "accepted-" + Guid.NewGuid().ToString("N"), TimeSpan.FromMinutes(2));
        try
        {
            await f.PublishAsync(f.Git.Root, "project");
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    // The server echoes the refused credentials as the user and the password, and in their Basic form.
    private static void AssertHidden(string details)
    {
        Assert.DoesNotContain("alice:a", details);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:a")), details);
    }
}
