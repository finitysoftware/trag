using Trag.Application.Ports.Threading;
using Trag.Application.Ports.Workspace;

namespace Trag.Application.Live;

// What every repository of the session is built over: the workspace it reads and writes through, the status bar its
// writes report to, the UI thread its progress reaches, and the clock its times and schedules read. None is a repository over nothing, which
// reads and writes nothing. Each part of the workspace a model goes through is named here once; a workspace without
// one, as a test double or a preview has, leaves the model that needs it reading or writing nothing.
public sealed record RepositoryServices(IRepositoryWorkspace? Workspace, StatusBarState StatusBar, IViewDispatcher Dispatcher)
{
    public static RepositoryServices None() => new(null, new(), new ImmediateViewDispatcher());

    public TimeProvider Time { get; init; } = TimeProvider.System;
    // Where a defect a write caught is recorded, so the sentence the user gets is not all that is left of it.
    public Action<Exception> Defects { get; init; } = _ => { };

    public ILiveRepositoryWorkspace? LiveWorkspace => Workspace as ILiveRepositoryWorkspace;
    public IBranchWorkspace? BranchWorkspace => Workspace as IBranchWorkspace;
    public ISyncWorkspace? SyncWorkspace => Workspace as ISyncWorkspace;
    public IHistoryWorkspace? HistoryWorkspace => Workspace as IHistoryWorkspace;
}
