using Trag.Application.Live;
using Trag.Application.Ports.Threading;
using Trag.Application.Workspace;
using Trag.Domain.Presentation.Text;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Revert;

// The revert question is about the commit asked last: the model says its title, its sentence and what keeps the revert
// back, so the dialog only shows them.
public sealed class RevertModelTests
{
    private static readonly string Older = Snapshots.Id('b');

    [Fact]
    public async Task WhenACommitIsAsked_ShouldSayTheQuestionAboutItAndWhatKeepsItBack()
    {
        var (workspace, repository) = await OpenAsync();
        await using var _ = workspace;
        Assert.Equal(("", ""), (repository.Revert.Title, repository.Revert.Question));

        repository.Revert.Ask(Snapshots.Commit(Older, "Older commit"));

        Assert.Equal(RevertText.Title(Older), repository.Revert.Title);
        Assert.Equal(RevertText.Question("main", "Older commit"), repository.Revert.Question);
        Assert.Equal(("", true), (repository.Revert.Refusal, repository.Revert.CanRevert));

        repository.Revert.Ask(Snapshots.Commit(Snapshots.HeadId, "Merge topic", Older, Snapshots.Id('c')));

        Assert.NotEqual("", repository.Revert.Refusal);
        Assert.False(repository.Revert.CanRevert);
    }

    private static async Task<(RepositoryWorkspace Workspace, Repository Repository)> OpenAsync()
    {
        var reader = new FakeRepositoryReader { Snapshot = Snapshots.Create() };
        var workspace = new RepositoryWorkspace(reader, writer: new FakeGitWriter(), historyReader: new FakeHistoryReader());
        var repository = new Repository(new RepositoryServices(workspace, new StatusBarState(), new ImmediateViewDispatcher()));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!);
        return (workspace, repository);
    }
}
