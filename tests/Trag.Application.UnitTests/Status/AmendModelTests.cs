using Trag.Application.Live;
using Trag.Application.Ports.Threading;
using Trag.Application.Workspace;
using Trag.Domain.Presentation.Text;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Status;

// An amend in progress belongs to the last commit it began over: it ends once HEAD moves and once the amend is written,
// and keeps the message it put aside for whoever shows it again.
public sealed class AmendModelTests
{
    [Fact]
    public async Task WhenHeadMovesDuringAnAmend_ShouldEndItAndKeepTheDraftItPutAside()
    {
        var (workspace, repository) = await OpenAsync();
        await using var _ = workspace;
        await repository.Amend.BeginAsync("draft");

        repository.Apply(repository.Snapshot! with { Head = Snapshots.Id('c') });

        Assert.Equal((false, "draft"), (repository.Amend.IsAmending, repository.Amend.Draft));
    }

    [Fact]
    public async Task WhenTheAmendIsWritten_ShouldEndIt()
    {
        var (workspace, repository) = await OpenAsync();
        await using var _ = workspace;
        await repository.Amend.BeginAsync("draft");

        await repository.Amend.AmendAsync("Better message");

        Assert.False(repository.Amend.IsAmending);
    }

    // Without the last commit's message there is nothing to amend from: the amend ends, the message being written comes
    // back, and the status bar says why, so Commit does not replace the last commit with the draft unasked.
    [Fact]
    public async Task WhenTheLastCommitCannotBeRead_ShouldEndTheAmendAndSaySo()
    {
        var history = new FakeHistoryReader { Details = _ => throw new IOException("unreadable") };
        var (workspace, repository, bar) = await OpenAsync(history);
        await using var _ = workspace;

        var message = await repository.Amend.BeginAsync("draft");

        Assert.Null(message);
        Assert.Equal((false, "draft"), (repository.Amend.IsAmending, repository.Amend.Draft));
        Assert.Equal(CommitText.AmendMessageNotRead, bar.Notice);
    }

    private static async Task<(RepositoryWorkspace Workspace, Repository Repository)> OpenAsync()
    {
        var (workspace, repository, _) = await OpenAsync(new FakeHistoryReader());
        return (workspace, repository);
    }

    private static async Task<(RepositoryWorkspace Workspace, Repository Repository, StatusBarState Bar)> OpenAsync(FakeHistoryReader history)
    {
        var reader = new FakeRepositoryReader { Snapshot = Snapshots.Create(files: [Snapshots.Staged("a.txt")]) };
        var workspace = new RepositoryWorkspace(reader, writer: new FakeGitWriter(), historyReader: history);
        var bar = new StatusBarState();
        var repository = new Repository(new RepositoryServices(workspace, bar, new ImmediateViewDispatcher()));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!);
        return (workspace, repository, bar);
    }
}
