using Trag.Domain.Branches;
using Trag.Domain.Publish;
using Trag.Domain.Status;
using Trag.TestSupport;
using Trag.ViewModels.Publish;

namespace Trag.ViewModels.UnitTests.Publish;

public sealed class PublishDraftTests
{
    [Fact]
    public void WhenTheModeOrTheRepositoryChanges_ShouldKeepTheInputAndRevalidateIt()
    {
        var draft = new PublishDraft(new(Snapshots.Create(), "fixture"), BranchCatalog.Empty)
        {
            Name = "my-project", IsPrivate = false, PushAfter = false,
            RemoteName = "upstream", RemoteUrl = "https://example.invalid/repo"
        };
        draft.Mode = PublishKind.AddRemote;
        Assert.True(draft.IsValid);
        draft.Mode = PublishKind.CreateRepository;
        Assert.Equal("my-project", draft.Name);
        Assert.False(draft.IsPrivate);
        Assert.False(draft.PushAfter);
        Assert.Equal("https://example.invalid/repo", draft.RemoteUrl);

        draft.UpdateContext(new(Snapshots.Create(remotes: [Remote("upstream")]), "renamed"));
        Assert.Equal("my-project", draft.Name);
        Assert.Contains("renamed", draft.Subtitle);
        Assert.True(draft.HasRemoteError);
        Assert.False(draft.IsValid);
    }

    [Fact]
    public async Task WhenTheTargetsAreUpdated_ShouldDeduplicateExactlySortStablyAndIndexOnlyTheCurrentRead()
    {
        await using var shell = ShellHarness.Compose(new FakePublishWorkspace());
        var branches = BranchCatalog.Create(new([], [
            Branch("origin", "z"), Branch("origin", "main"), Branch("origin", "a"),
            Branch("origin", "A"), Branch("origin", "a"), Branch("upstream", "other"),
            Branch("Origin", "different")
        ], "main"), Snapshots.Create());
        var targets = new PublishTargetList();
        targets.Update(branches, "origin", "main");
        Assert.Equal(["main", "a", "A", "z"], targets.Items.Select(item => item.Name));
        Assert.False(targets.Selected!.IsNew);
        var items = targets.Items;
        targets.Update(branches, "origin", "main");
        Assert.Same(items, targets.Items);

        targets.Update(branches, "upstream", "main");
        Assert.Equal(["main", "other"], targets.Items.Select(item => item.Name));
        Assert.True(targets.Selected!.IsNew);
        targets.Update(branches, "Origin", "main");
        Assert.Equal(["main", "different"], targets.Items.Select(item => item.Name));
        targets.Update(BranchCatalog.Empty, "origin", "main");
        Assert.Equal("main", Assert.Single(targets.Items).Name);
        Assert.True(targets.Selected!.IsNew);
    }

    [Fact]
    public async Task WhenNewReadsOrAnotherRemoteFollow_ShouldKeepTheTargetChoiceUntilItDisappears()
    {
        await using var shell = ShellHarness.Compose(new FakePublishWorkspace());
        var branches = BranchCatalog.Create(new([], [Branch("origin", "release"), Branch("upstream", "release")], "main"), Snapshots.Create());
        var targets = new PublishTargetList();
        targets.Update(branches, "origin", "main");
        targets.Selected = targets.Items[1];
        targets.Update(branches, "upstream", "main");
        Assert.Equal("release", targets.Selected!.Name);
        Assert.Equal("upstream", targets.Selected.Remote);
        Assert.True(targets.HasWarning);

        var changes = new List<string?>();
        targets.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        targets.Update(branches, "upstream", "topic");
        Assert.Equal("release", targets.Selected.Name);
        Assert.Contains("topic", targets.Warning);
        Assert.Contains("topic", targets.Summary);
        Assert.Contains(nameof(targets.Warning), changes);
        Assert.Contains(nameof(targets.Summary), changes);

        targets.Update(BranchCatalog.Empty, "upstream", "topic");
        Assert.Equal("topic", targets.Selected!.Name);
        Assert.False(targets.HasWarning);
        targets.Update(branches, null, "topic");
        Assert.Empty(targets.Items);
        Assert.Null(targets.Selected);
        targets.Update(branches, "origin", null);
        Assert.Empty(targets.Items);
        Assert.Null(targets.Selected);
    }

    [Fact]
    public void WhenTheRemotesAreReadAgain_ShouldKeepTheChosenNameWithItsNewAddressAndFallBackWhenItDisappears()
    {
        var draft = new PublishDraft(new(Snapshots.Create(remotes: [Remote("upstream"), Remote("origin")]), "fixture"), BranchCatalog.Empty);
        Assert.Equal("origin", draft.ExistingRemote!.Name);
        Assert.True(draft.IsValid);
        var renamedAddress = new RepositoryRemote("origin", "new-fetch", "new-push");
        draft.UpdateContext(new(Snapshots.Create(remotes: [renamedAddress, Remote("upstream")]), "fixture"));
        Assert.Same(renamedAddress, draft.ExistingRemote);
        draft.UpdateContext(new(Snapshots.Create(remotes: [Remote("upstream")]), "fixture"));
        Assert.Equal("upstream", draft.ExistingRemote!.Name);
        Assert.True(draft.IsValid);
        draft.UpdateContext(new(Snapshots.Create(), "fixture"));
        Assert.Null(draft.ExistingRemote);
        Assert.Empty(draft.Targets.Items);
        Assert.False(draft.IsValid);
    }

    // While origin is taken the new remote is named after the repository, and keeps that name as the user types the
    // repository's, until the user names the remote.
    [Fact]
    public void WhenOriginIsTaken_ShouldNameTheNewRemoteAsTheUserNamesTheRepositoryUntilTheRemoteIsNamed()
    {
        var draft = new PublishDraft(new(Snapshots.Create(remotes: [Remote("origin")]), "fixture"), BranchCatalog.Empty) { Mode = PublishKind.CreateRepository };
        draft.Name = "sample-app";
        Assert.Equal("sample-app", draft.RemoteName);
        draft.RemoteName = "sample";
        draft.Name = "other";
        Assert.Equal("sample", draft.RemoteName);
    }

    // With origin free the hidden remote name stays origin, even after a repository name that passed through "origin".
    [Fact]
    public void WhenOriginIsFree_ShouldKeepOriginWhateverTheRepositoryIsNamed()
    {
        var draft = new PublishDraft(new(Snapshots.Create(), "fixture"), BranchCatalog.Empty) { Name = "origin" };
        draft.Name = "origin2";
        Assert.Equal("origin", draft.RemoteName);
    }

    private static RepositoryRemote Remote(string name) => new(name, "https://example.invalid/" + name, "https://example.invalid/" + name);
    private static BranchInfo Branch(string remote, string name) => new(remote + "/" + name, "refs/remotes/" + remote + "/" + name, Snapshots.HeadId, true, remote) { NameOnRemote = name };
}
