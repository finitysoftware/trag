using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Sync;
using Trag.Infrastructure.Git.Sync;

namespace Trag.Infrastructure.UnitTests.Git.Sync;

// The upstream branches a remote offers, as the sync panel lists them: bounded by the branch limit, and always with the
// one that is chosen.
public sealed class SyncBranchListTests
{
    private static readonly RemoteRefspecMapping Mapping = new("refs/heads/*", "refs/remotes/origin/*");

    // More branches than the panel lists, and the chosen upstream sorts after the limit: it is still listed, and the
    // panel says the remote has too many branches.
    [Fact]
    public void WhenThereAreMoreFetchedRefsThanTheLimit_ShouldKeepTheChosenUpstreamAndSayTooManyBranches()
    {
        var list = new SyncBranchList([Mapping]);
        for (var i = 0; i <= GitLimits.BranchCount; i++)
            list.AddFetched("refs/remotes/origin/b-" + i.ToString("D5") + "\0" + new string('a', 40) + "\0");
        var chosen = list.Choose("refs/heads/b-" + GitLimits.BranchCount.ToString("D5"))!;

        var visible = list.Visible(chosen);

        Assert.Contains(chosen, visible);
        Assert.Equal(GitLimits.BranchCount + 1, visible.Length);
        Assert.Equal(SyncText.TooManyBranches, list.Message(new("origin", "fetch", "push", "config"), chosen));
    }
}
