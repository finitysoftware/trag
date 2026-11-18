using Trag.Application.Live;
using Trag.Application.Ports.Threading;
using Trag.Application.Workspace;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Tags;

// The repository's tags over the real workspace and fake Git: the list follows the tags' fingerprint in the snapshot.
public sealed class TagsModelTests
{
    [Fact]
    public async Task WhenTheRepositoryOpensWithTags_ShouldReadTheList()
    {
        var (branches, _, workspace, repository) = await OpenWithOneTagAsync();
        await using var _ = workspace;

        Assert.True(repository.Tags.HasTags);
        Assert.Equal(["v1.0"], repository.Tags.List.Value.Select(tag => tag.Name));
        Assert.Equal(1, branches.TagCalls);
    }

    [Fact]
    public async Task WhenTheTagsStayTheSame_ShouldNotReadTheListAgain()
    {
        var (branches, _, workspace, repository) = await OpenWithOneTagAsync();
        await using var _ = workspace;

        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!); await repository.Tags.List.Completion;

        Assert.Equal(1, branches.TagCalls);
    }

    [Fact]
    public async Task WhenATagComes_ShouldReadTheListAgain()
    {
        var (branches, reader, workspace, repository) = await OpenWithOneTagAsync();
        await using var _ = workspace;

        branches.Tags.Insert(0, new("v2.0", Snapshots.HeadId, "Second"));
        reader.Snapshot = reader.Snapshot with { TagRefs = "two" };
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!); await repository.Tags.List.Completion;

        Assert.Equal(2, branches.TagCalls);
        Assert.Equal(["v2.0", "v1.0"], repository.Tags.List.Value.Select(tag => tag.Name));
    }

    [Fact]
    public async Task WhenTheLastTagIsGone_ShouldEmptyTheListWithoutReadingIt()
    {
        var (branches, reader, workspace, repository) = await OpenWithOneTagAsync();
        await using var _ = workspace;

        reader.Snapshot = reader.Snapshot with { TagRefs = "" };
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!); await repository.Tags.List.Completion;

        Assert.False(repository.Tags.HasTags);
        Assert.Empty(repository.Tags.List.Value);
        Assert.Equal(1, branches.TagCalls);
    }

    // v1.0 on HEAD, opened.
    private static async Task<(FakeBranchReader Branches, FakeRepositoryReader Reader, RepositoryWorkspace Workspace, Repository Repository)> OpenWithOneTagAsync()
    {
        var branches = new FakeBranchReader { Tags = { new("v1.0", Snapshots.HeadId, "First") } };
        var reader = new FakeRepositoryReader { Snapshot = Snapshots.Create() with { TagRefs = "one" } };
        var workspace = new RepositoryWorkspace(reader, branchReader: branches);
        var repository = new Repository(new RepositoryServices(workspace, new StatusBarState(), new ImmediateViewDispatcher()));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!); await repository.Tags.List.Completion;
        return (branches, reader, workspace, repository);
    }
}
