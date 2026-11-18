using Trag.Application.Live;
using Trag.Application.Ports.Threading;
using Trag.Application.Workspace;
using Trag.Domain.Presentation.Text;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.CherryPick;

// The cherry-pick question is about the commit asked last: the model says its title and its sentence beside what keeps
// the cherry-pick back, so the dialog only shows them.
public sealed class CherryPickModelTests
{
    [Fact]
    public async Task WhenACommitIsAsked_ShouldSayTheQuestionAboutIt()
    {
        var reader = new FakeRepositoryReader { Snapshot = Snapshots.Create() };
        await using var workspace = new RepositoryWorkspace(reader, writer: new FakeGitWriter(), historyReader: new FakeHistoryReader());
        var repository = new Repository(new RepositoryServices(workspace, new StatusBarState(), new ImmediateViewDispatcher()));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!);
        Assert.Equal("", repository.CherryPick.Question);

        await repository.CherryPick.AskAsync(Snapshots.Commit(Snapshots.Id('d'), "Other change"));

        Assert.Equal(CherryPickText.Question("main", "Other change"), repository.CherryPick.Question);
    }
}
