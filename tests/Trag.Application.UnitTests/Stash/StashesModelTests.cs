using Trag.Application.Live;
using Trag.Application.Ports.Threading;
using Trag.Application.Workspace;
using Trag.Domain.Stash;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Stash;

// The repository's stashes over the real workspace and fake Git: when the list reads, and what each write asks Git for.
public sealed class StashesModelTests
{
    [Fact]
    public async Task WhenNoRepositoryIsOpenYet_ShouldHaveNoStashes()
    {
        var stashes = new FakeStashReader(); var first = stashes.Add("On main: spike");
        var reader = new FakeRepositoryReader { Snapshot = Snapshots.Create(stashHead: first.Id) };
        await using var workspace = new RepositoryWorkspace(reader, stashReader: stashes);
        var repository = new Repository(Services(workspace));
        Assert.False(repository.Stashes.HasStashes); Assert.Empty(repository.Stashes.List.Value);
    }

    [Fact]
    public async Task WhenTheRepositoryOpensWithAStash_ShouldReadTheList()
    {
        var (_, _, workspace, repository, first) = await OpenWithOneStashAsync();
        await using var _ = workspace;
        Assert.True(repository.Stashes.HasStashes); Assert.Equal([first.Id], repository.Stashes.List.Value.Select(entry => entry.Id));
    }

    [Fact]
    public async Task WhenRefsStashStaysTheSame_ShouldNotReadTheListAgain()
    {
        var (stashes, _, workspace, repository, _) = await OpenWithOneStashAsync();
        await using var _ = workspace;

        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!); await repository.Stashes.List.Completion;

        Assert.Equal(1, stashes.Calls);
    }

    [Fact]
    public async Task WhenRefsStashMoves_ShouldReadTheListAgain()
    {
        var (stashes, reader, workspace, repository, first) = await OpenWithOneStashAsync();
        await using var _ = workspace;

        var second = stashes.Add("On main: another");
        reader.Snapshot = Snapshots.Create(stashHead: second.Id);
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!); await repository.Stashes.List.Completion;

        Assert.Equal(2, stashes.Calls); Assert.Equal([second.Id, first.Id], repository.Stashes.List.Value.Select(entry => entry.Id));
    }

    // The list was read twice before: at the open, and when a second stash moved refs/stash.
    [Fact]
    public async Task WhenRefsStashIsGone_ShouldEmptyTheListWithoutReadingIt()
    {
        var (stashes, reader, workspace, repository, _) = await OpenWithOneStashAsync();
        await using var _ = workspace;
        var second = stashes.Add("On main: another");
        reader.Snapshot = Snapshots.Create(stashHead: second.Id);
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!); await repository.Stashes.List.Completion;

        reader.Snapshot = Snapshots.Create();
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!); await repository.Stashes.List.Completion;

        Assert.False(repository.Stashes.HasStashes); Assert.Empty(repository.Stashes.List.Value); Assert.Equal(2, stashes.Calls);
    }

    [Fact]
    public async Task WhenChangesAreSetAside_ShouldAskGitToPushThemWithTheChosenOptions()
    {
        var (writer, workspace, repository) = await OpenWithAChangeAsync();
        await using var _ = workspace;
        Assert.True(repository.Stashes.CanPush);

        await repository.Stashes.PushAsync(new("spike", IncludeUntracked: true));

        var requests = writer.Requests.Cast<StashRequest>().ToArray();
        Assert.Equal(new StashOperation("spike", IncludeUntracked: true), requests[0].Stash);
    }

    // A push ran first, so the apply is the second request.
    [Fact]
    public async Task WhenAStashIsApplied_ShouldAskGitToApplyThatEntry()
    {
        var (writer, workspace, repository) = await OpenWithAChangeAsync();
        await using var _ = workspace;
        await repository.Stashes.PushAsync(new("spike", IncludeUntracked: true));

        await repository.Stashes.ApplyAsync("stash-id");

        var requests = writer.Requests.Cast<StashRequest>().ToArray();
        Assert.Equal("stash-id", requests[1].Stash.Id);
    }

    // A push and an apply ran first, each its own request through the repository's writer.
    [Fact]
    public async Task WhenAStashIsDropped_ShouldAskGitToDropThatEntry()
    {
        var (writer, workspace, repository) = await OpenWithAChangeAsync();
        await using var _ = workspace;
        await repository.Stashes.PushAsync(new("spike", IncludeUntracked: true));
        await repository.Stashes.ApplyAsync("stash-id");

        await repository.Stashes.DropAsync("stash-id");

        var requests = writer.Requests.Cast<StashRequest>().ToArray();
        Assert.Equal([StashAction.Push, StashAction.Apply, StashAction.Drop], requests.Select(request => request.Action));
        Assert.Equal("stash-id", requests[2].Stash.Id);
    }

    [Fact]
    public void WhenTheWorkingTreeIsClean_ShouldNotSetChangesAside()
    {
        var clean = new Repository(hasWriter: true); clean.Apply(Snapshots.Create());
        Assert.False(clean.Stashes.CanPush);
    }

    [Fact]
    public void WhenThereAreChanges_ShouldSetThemAside()
    {
        var changed = new Repository(hasWriter: true); changed.Apply(Snapshots.Create(files: [Snapshots.Untracked("new.txt")]));
        Assert.True(changed.Stashes.CanPush);
    }

    [Fact]
    public void WhenTheRepositoryTakesNoWrite_ShouldNotSetChangesAside()
    {
        var readOnly = new Repository(); readOnly.Apply(Snapshots.Create(files: [Snapshots.Untracked("new.txt")]));
        Assert.False(readOnly.Stashes.CanPush);
    }

    // A merge in progress takes no ordinary write.
    [Fact]
    public void WhenAMergeIsInProgress_ShouldNotSetChangesAside()
    {
        var merging = new Repository(hasWriter: true);
        merging.Apply(Snapshots.Create(files: [Snapshots.Conflict("a.txt")], operation: RepositoryOperation.Merge, merge: new("incoming", Snapshots.HeadId, "Merge", "fp", "")));
        Assert.False(merging.Stashes.CanPush);
    }

    // One stash at refs/stash, opened with the list read once.
    private static async Task<(FakeStashReader Stashes, FakeRepositoryReader Reader, RepositoryWorkspace Workspace, Repository Repository, StashEntry First)> OpenWithOneStashAsync()
    {
        var stashes = new FakeStashReader(); var first = stashes.Add("On main: spike");
        var reader = new FakeRepositoryReader { Snapshot = Snapshots.Create(stashHead: first.Id) };
        var workspace = new RepositoryWorkspace(reader, stashReader: stashes);
        var repository = new Repository(Services(workspace));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!); await repository.Stashes.List.Completion;
        return (stashes, reader, workspace, repository, first);
    }

    // a.txt modified, opened.
    private static async Task<(FakeGitWriter Writer, RepositoryWorkspace Workspace, Repository Repository)> OpenWithAChangeAsync()
    {
        var writer = new FakeGitWriter();
        var reader = new FakeRepositoryReader { Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]) };
        var workspace = new RepositoryWorkspace(reader, writer: writer, stashReader: new FakeStashReader());
        var repository = new Repository(Services(workspace));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!);
        return (writer, workspace, repository);
    }

    private static RepositoryServices Services(RepositoryWorkspace workspace) =>
        new(workspace, new StatusBarState(), new ImmediateViewDispatcher());
}
