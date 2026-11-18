using Trag.Application.Live;
using Trag.Application.Ports.Threading;
using Trag.Application.Workspace;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Accounts;

// A remote branch whose Git LFS content is only on the server, behind authentication, switched to from the branch list
// over real Git and Git LFS. Git trusts the server's certificate through settings given to the whole process, so the
// class runs alone.
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class BranchLfsTests
{
    private readonly string aliceToken = "alice-" + Guid.NewGuid().ToString("N");

    // The content comes down with the account the remote uses, or with the user's own credentials when it uses none, and
    // the new branch has the file itself. A refused download says so and leaves the user where they were, with nothing of
    // the branch half written.
    [Theory]
    [InlineData("account")]
    [InlineData("own credentials")]
    [InlineData("refused")]
    public async Task WhenSwitchingToARemoteBranchWhoseLfsContentIsNotLocal_ShouldUseTheAccountOrSayWhy(string credentials)
    {
        using var f = await HttpsRemoteFixture.CreateAsync("alice", credentials == "refused" ? "other-" + Guid.NewGuid().ToString("N") : aliceToken, TimeSpan.FromMinutes(2));
        await PublishTopicWithLfsContentOnlyThereAsync(f);
        using var accounts = await f.SignInAsync(("alice", aliceToken));
        var sync = f.Sync(credentials == "own credentials" ? null : accounts);
        if (credentials == "own credentials")
        {
            var store = Path.Combine(f.Container, "credentials");
            await File.WriteAllTextAsync(store, f.Server.Url("").Replace("https://", "https://alice:" + aliceToken + "@", StringComparison.Ordinal) + "\n", f.Token);
            await f.Git.GitAsync("config", "credential.helper", "store --file=\"" + store.Replace('\\', '/') + "\"");
        }
        else
        {
            var alice = (await accounts.AccountsAsync(f.Token)).Single();
            Assert.Equal(GitWriteOutcome.Succeeded, (await f.ExecuteAsync(sync, SyncAction.SelectAccount, alice.Id)).Outcome);
        }
        var reader = new CliRepositoryReader(f.Git.Runner);
        await using var workspace = new RepositoryWorkspace(reader, writer: new GitWriteRouter(f.Git.Runner, sync, reader), branchReader: new CliBranchReader(f.Git.Runner));
        var repository = new Repository(new RepositoryServices(workspace, new StatusBarState(), new ImmediateViewDispatcher()));
        repository.Apply((await workspace.OpenAsync(f.Git.Root, f.Token))!);
        await repository.Branches.List.Completion;

        var switched = await repository.Branches.SwitchToAsync(repository.Branches.List.Value.Remote.Single(branch => branch.Name == "origin/topic"));

        if (credentials == "refused")
        {
            Assert.Equal((GitWriteOutcome.Rejected, BranchOutcomeText.LfsNotDownloaded), (switched!.Outcome, switched.Message));
            Assert.Equal(("main", ""), (await f.Git.GitAsync("branch", "--show-current"), await f.Git.GitAsync("status", "--porcelain")));
        }
        else
        {
            Assert.Equal((GitWriteOutcome.Succeeded, BranchOutcomeText.CreatedAndOn("topic")), (switched!.Outcome, switched.Message));
            Assert.Equal("lfs content\n", await File.ReadAllTextAsync(Path.Combine(f.Git.Root, "asset.bin"), f.Token));
        }
        Assert.Equal(["alice:" + aliceToken], f.Server.Credentials.OfType<string>().Distinct());
    }

    // The fixture's repository on main, with origin at the server; origin's topic adds an LFS file whose content only the
    // server holds, in the lfs folder of the bare repository, where Git LFS keeps it.
    private static async Task PublishTopicWithLfsContentOnlyThereAsync(HttpsRemoteFixture f)
    {
        var (root, bare) = (f.Git.Root, f.Bare("project"));
        await LfsConfig.SetAsync(f.Git, root, installed: true);
        await f.Git.WriteAsync("readme.txt", "readme\n");
        await f.Git.CommitAsync("Main", "readme.txt");
        await f.Git.GitAsync("switch", "-c", "topic");
        await f.Git.WriteAsync(".gitattributes", "*.bin filter=lfs diff=lfs merge=lfs -text\n");
        await f.Git.WriteAsync("asset.bin", "lfs content\n");
        await f.Git.CommitAsync("Asset", ".gitattributes", "asset.bin");
        await f.Git.GitAsync("switch", "main");
        await f.Git.GitAtAsync(f.Container, "clone", "--bare", "--", root, bare);
        Directory.CreateDirectory(Path.Combine(bare, "lfs"));
        Directory.Move(Path.Combine(root, ".git", "lfs", "objects"), Path.Combine(bare, "lfs", "objects"));
        await f.Git.GitAsync("branch", "-D", "topic");
        await f.Git.GitAsync("remote", "add", "origin", bare);
        await f.Git.GitAsync("fetch", "origin");
        await f.Git.GitAsync("remote", "set-url", "origin", f.Server.Url("team/project.git"));
        await f.Git.GitAsync("branch", "--set-upstream-to=origin/main");
    }
}
