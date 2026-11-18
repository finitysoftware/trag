using Trag.Application.Live;
using Trag.Application.Ports.Threading;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Live;

// Who takes down what the status bar shows. An open and the close take down everything said about the repository, a
// refresh what the last read and write left (the last sync keeps its own report), a write the error and the reports
// it replaces, and every snapshot the repository warning, which follows the repository's state. Nothing but an open,
// a refresh and the close takes a notice down; a component that raised one of its own takes down only that one.
public sealed class StatusBarOwnershipTests
{
    private const string Folder = "fixture";

    public static TheoryData<string, string[]> Kept => new()
    {
        { "open", [] },
        { "refresh", [nameof(StatusBarState.SyncDetails), nameof(StatusBarState.SyncOperationText)] },
        { "close", [] },
        { "stage", [nameof(StatusBarState.Notice)] },
        { "fetch", [nameof(StatusBarState.Notice), nameof(StatusBarState.WriteDetails)] },
        { "live update", [nameof(StatusBarState.ErrorMessage), nameof(StatusBarState.Notice), nameof(StatusBarState.WriteDetails),
            nameof(StatusBarState.SyncDetails), nameof(StatusBarState.SyncOperationText)] },
    };

    [Theory]
    [MemberData(nameof(Kept))]
    public async Task WhenTheRepositoryIsReadWrittenOrClosed_ShouldTakeDownOnlyWhatThatEventOwns(string happening, string[] kept)
    {
        var workspace = new FakeRepositoryWorkspace
        {
            Snapshot = Snapshots.Create(root: Folder),
            Result = new(GitWriteOutcome.Succeeded, "Done") { Snapshot = Snapshots.Create(root: Folder) }
        };
        var bar = new StatusBarState();
        await using var workbench = new Workbench(new(workspace, bar, new ImmediateViewDispatcher()), new FakeLastRepositoryStore(), new FakeRepositoryCatalog(), null);
        workbench.Attach(_ => { });
        await workbench.Session.OpenRepositoryAsync(Folder);
        var fields = new[] { nameof(StatusBarState.ErrorMessage), nameof(StatusBarState.Notice), nameof(StatusBarState.RepositoryWarning),
            nameof(StatusBarState.WriteDetails), nameof(StatusBarState.SyncDetails), nameof(StatusBarState.SyncOperationText) };
        foreach (var field in fields) typeof(StatusBarState).GetProperty(field)!.SetValue(bar, field);

        var repository = workbench.Session.Current;
        var shown = repository.Snapshot!;
        await (happening switch
        {
            "open" => workbench.Session.OpenRepositoryAsync(Folder),
            "refresh" => workbench.Session.RefreshAsync(),
            "close" => workbench.Session.CloseRepositoryAsync(),
            "stage" => repository.Writer.RunAsync(new StageRequest(shown, [Snapshots.Path("a")])),
            "fetch" => repository.Writer.RunAsync(new SyncRequest(shown, FakeGitSync.DefaultState(shown, "origin", null), SyncAction.Fetch)),
            _ => workbench.Session.UpdateSnapshotAsync(Snapshots.Create(root: Folder), CancellationToken.None)
        });

        Assert.Equal(kept, fields.Where(field => (string?)typeof(StatusBarState).GetProperty(field)!.GetValue(bar) == field));
    }
}
