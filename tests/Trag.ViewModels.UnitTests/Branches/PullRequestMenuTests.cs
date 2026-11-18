using Trag.Domain.Presentation.Text;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.Branches;

// A branch's menu starts a pull request on GitHub, or a merge request on GitLab, on the provider's own page in the
// browser, for a local branch whose upstream is on that provider; the picker closes as the page opens.
public sealed class PullRequestMenuTests
{
    private static async Task<ShellViewModel> OpenedAsync(ShellHarness h, string address)
    {
        h.Reader.Snapshot = Snapshots.Create(remotes: [Snapshots.Remote("origin", address)], trackingRemote: "origin", upstream: "origin/main");
        h.Branches.Local.Add("topic");
        var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Branches.List.Completion;
        return model;
    }

    [Fact]
    public async Task WhenABranchOnGitHubIsChosen_ShouldOpenItsPullRequestPageAndCloseThePicker()
    {
        await using var model = await OpenedAsync(new ShellHarness(), "https://github.com/finity/trag.git");
        var host = new RecordingHost();
        model.Host = host;
        model.Branches.IsPickerOpen = true;

        Assert.Equal((BranchText.CreatePullRequest, "github"), (model.Branches.Rows.Current!.RequestLabel, model.Branches.Rows.Current.RequestIcon));
        model.Branches.CreatePullRequestCommand.Execute(model.Branches.Rows.Current);

        Assert.Equal(["https://github.com/finity/trag/compare/main?expand=1"], host.Opened.Select(uri => uri.AbsoluteUri));
        Assert.False(model.Branches.IsPickerOpen);
    }

    [Fact]
    public async Task WhenABranchHasNoUpstreamOnTheProvider_ShouldOfferNoPage()
    {
        await using var model = await OpenedAsync(new ShellHarness(), "https://github.com/finity/trag.git");

        Assert.False(model.Branches.CreatePullRequestCommand.CanExecute(model.Branches.Rows.Local.Single(row => row.Name == "topic")));
    }

    // A branch with no page yet is offered the request as the repository's preferred remote names it.
    [Fact]
    public async Task WhenTheRemoteIsOnGitLab_ShouldCallItAMergeRequest()
    {
        await using var model = await OpenedAsync(new ShellHarness(), "git@gitlab.com:finity/trag.git");
        var topic = model.Branches.Rows.Local.Single(row => row.Name == "topic");

        Assert.Equal((BranchText.CreateMergeRequest, "gitlab"), (model.Branches.Rows.Current!.RequestLabel, model.Branches.Rows.Current.RequestIcon));
        Assert.True(model.Branches.CreatePullRequestCommand.CanExecute(model.Branches.Rows.Current));
        Assert.Equal((BranchText.CreateMergeRequest, "gitlab", false), (topic.RequestLabel, topic.RequestIcon, model.Branches.CreatePullRequestCommand.CanExecute(topic)));
    }

    // A branch's menu names its request as the provider of its own upstream does, whichever remote the repository prefers,
    // and opens that provider's page.
    [Fact]
    public async Task WhenTheUpstreamIsOnAnotherProviderThanOrigin_ShouldNameAndOpenTheUpstreamsRequest()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(remotes: [Snapshots.Remote("origin", "https://github.com/finity/trag.git"), Snapshots.Remote("lab", "https://gitlab.com/finity/trag.git")],
            trackingRemote: "lab", upstream: "lab/main");
        h.Branches.Upstreams["main"] = "refs/remotes/lab/main";
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Branches.List.Completion;
        var host = new RecordingHost();
        model.Host = host;

        var current = model.Branches.Rows.Current!;
        model.Branches.CreatePullRequestCommand.Execute(current);

        Assert.Equal((BranchText.CreateMergeRequest, "gitlab"), (current.RequestLabel, current.RequestIcon));
        Assert.Equal(["https://gitlab.com/finity/trag/-/merge_requests/new?merge_request%5Bsource_branch%5D=main"], host.Opened.Select(uri => uri.AbsoluteUri));
    }

    // An address changed in the remotes dialog counts at once, for the page and for what the menu calls it.
    [Fact]
    public async Task WhenTheRemotesAddressChanges_ShouldOfferTheRequestOfTheNewAddress()
    {
        var h = new ShellHarness();
        await using var model = await OpenedAsync(h, "https://github.com/finity/trag.git");
        var host = new RecordingHost();
        model.Host = host;

        h.Reader.Snapshot = h.Reader.Snapshot with { Remotes = [Snapshots.Remote("origin", "git@gitlab.com:finity/trag.git")] };
        await model.RefreshCommand.ExecuteAsync(null);
        await model.Branches.List.Completion;
        var current = model.Branches.Rows.Current!;
        model.Branches.CreatePullRequestCommand.Execute(current);

        Assert.Equal((BranchText.CreateMergeRequest, "gitlab"), (current.RequestLabel, current.RequestIcon));
        Assert.Equal(["https://gitlab.com/finity/trag/-/merge_requests/new?merge_request%5Bsource_branch%5D=main"], host.Opened.Select(uri => uri.AbsoluteUri));
    }

}
