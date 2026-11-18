using Trag.Domain.Accounts;
using Trag.Domain.Branches;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Branches;

// The provider's page that starts a pull request (GitHub) or a merge request (GitLab) from a local branch: on the remote
// its upstream lives on, by the branch's name there, from the remote's HTTPS, ssh:// or git@host:path address (also as
// the snapshot shows it, without the user) and with nothing of the address's credentials. A branch with no upstream on a remote, or a remote on another host, has none.
public sealed class PullRequestPageTests
{
    private static BranchInfo Local(string name, string? upstream) => new(name, "refs/heads/" + name, Snapshots.Id('a'), false, null, upstream);

    private static string? Page(string address, string upstream = "refs/remotes/origin/feature/blame", string remote = "origin") =>
        PullRequestPage.For(Local("blame", upstream), [new RepositoryRemote(remote, address, address)])?.Address.AbsoluteUri;

    [Theory]
    [InlineData("https://github.com/finity/trag.git")]
    [InlineData("https://user:secret@github.com/finity/trag")]
    [InlineData("git@github.com:finity/trag.git")]
    [InlineData("github.com:finity/trag.git")]
    [InlineData("ssh://git@github.com/finity/trag.git")]
    [InlineData("ssh://git@ssh.github.com:443/finity/trag.git")]
    public void WhenTheUpstreamIsOnGitHub_ShouldOpenTheComparePageOfItsBranchThere(string address) =>
        Assert.Equal("https://github.com/finity/trag/compare/feature/blame?expand=1", Page(address));

    // A self-hosted GitLab keeps its installation path; an SSH port is not the web's, nor is GitLab.com's SSH host on the HTTPS port.
    [Theory]
    [InlineData("https://gitlab.company.com/tools/trag.git", "https://gitlab.company.com/tools/trag")]
    [InlineData("https://gitlab.company.com:8443/tools/trag.git", "https://gitlab.company.com:8443/tools/trag")]
    [InlineData("ssh://git@gitlab.com:2222/group/sub/trag.git", "https://gitlab.com/group/sub/trag")]
    [InlineData("ssh://git@altssh.gitlab.com:443/group/sub/trag.git", "https://gitlab.com/group/sub/trag")]
    public void WhenTheUpstreamIsOnGitLab_ShouldOpenTheNewMergeRequestPageOfItsBranch(string address, string project) =>
        Assert.Equal(project + "/-/merge_requests/new?merge_request%5Bsource_branch%5D=feature%2Fblame", Page(address));

    // A name with characters an address gives a meaning, or with letters outside ASCII, reaches the page as the name itself.
    [Theory]
    [InlineData("https://github.com/finity/trag.git", "fix/#12", "https://github.com/finity/trag/compare/fix/%2312?expand=1")]
    [InlineData("https://gitlab.com/finity/trag.git", "fix/#12", "https://gitlab.com/finity/trag/-/merge_requests/new?merge_request%5Bsource_branch%5D=fix%2F%2312")]
    [InlineData("https://github.com/finity/trag.git", "fix/50%25-\u010Dvor", "https://github.com/finity/trag/compare/fix/50%2525-%C4%8Dvor?expand=1")]
    [InlineData("https://gitlab.com/finity/trag.git", "fix/50%25-\u010Dvor", "https://gitlab.com/finity/trag/-/merge_requests/new?merge_request%5Bsource_branch%5D=fix%2F50%2525-%C4%8Dvor")]
    public void WhenTheBranchNameHasCharactersAnAddressGivesAMeaning_ShouldEscapeThem(string address, string name, string page) =>
        Assert.Equal(page, Page(address, "refs/remotes/origin/" + name));

    [Theory]
    [InlineData("https://bitbucket.org/finity/trag.git", "refs/remotes/origin/feature/blame")]
    [InlineData("https://github.com/finity/trag.git", null)]
    [InlineData("https://github.com/finity/trag.git", "refs/heads/main")]
    [InlineData("https://github.com/finity/trag.git", "refs/remotes/fork/feature/blame")]
    [InlineData("https://github.com/trag", "refs/remotes/origin/feature/blame")]
    public void WhenThereIsNoProviderPageForTheBranch_ShouldHaveNone(string address, string? upstream) => Assert.Null(Page(address, upstream!));

    [Theory]
    [InlineData("git@github.com:finity/trag.git", AccountProvider.GitHub)]
    [InlineData("github.com:finity/trag.git", AccountProvider.GitHub)]
    [InlineData("ssh://git@ssh.github.com:443/finity/trag.git", AccountProvider.GitHub)]
    [InlineData("https://gitlab.company.com/tools/trag.git", AccountProvider.GitLab)]
    [InlineData("https://bitbucket.org/finity/trag.git", null)]
    public void WhenAnAddressIsNamed_ShouldTellItsProvider(string address, AccountProvider? provider) => Assert.Equal(provider, PullRequestPage.ProviderOf(address));

    // A remote may be named with a slash, so the upstream belongs to the longest remote its name starts with.
    [Fact]
    public void WhenRemotesShareAPrefix_ShouldUseTheLongestMatch() =>
        Assert.Equal("https://github.com/fork/trag/compare/topic?expand=1", PullRequestPage.For(Local("topic", "refs/remotes/gh/fork/topic"),
            [new("gh", "https://github.com/finity/trag.git", "https://github.com/finity/trag.git"), new("gh/fork", "https://github.com/fork/trag.git", "https://github.com/fork/trag.git")])?.Address.AbsoluteUri);

    // The provider a menu names is the page's own, from the same remote, whichever remote the repository prefers.
    [Fact]
    public void WhenTheLabelAndThePageCouldDisagree_ShouldUseOneRemoteForBoth()
    {
        var page = PullRequestPage.For(Local("topic", "refs/remotes/lab/topic"),
            [new("origin", "https://github.com/finity/trag.git", "https://github.com/finity/trag.git"), new("lab", "https://gitlab.com/finity/trag.git", "https://gitlab.com/finity/trag.git")]);

        Assert.Equal((AccountProvider.GitLab, "https://gitlab.com/finity/trag/-/merge_requests/new?merge_request%5Bsource_branch%5D=topic"), (page?.Provider, page?.Address.AbsoluteUri));
    }
}
