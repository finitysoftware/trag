using System.ComponentModel;
using Trag.Application.Accounts;
using Trag.Application.Catalog;
using Trag.Application.Ports.Catalog;
using Trag.Application.Ports.Storage;
using Trag.Application.Sync;
using Trag.Domain.Presentation.Text;

namespace Trag.Application.Live;

// The application's models, whichever repository is open, as Repository is the model of one: the session with the open
// repository, what Cancel reaches beyond the repository, the status bar, the saved layout, the
// repository catalog, the accounts and the automatic fetch. The catalog records every repository the session opens, and the automatic fetch follows every one it starts.
// The session starts its first, empty repository before anyone can show it, so what it starts is shown from Attach on,
// the current one first. It also knows how the application starts, whether it may close, and how it goes. When the
// accounts were edited, until it stops, the open repository reads its sync target again (unless a read runs or it
// cannot), and AccountsEdited tells the features.
public sealed class Workbench : IAsyncDisposable
{
    private Action<Repository>? show;
    private Repository? shown;
    private bool started;

    public Workbench(RepositoryServices services, ILastRepositoryStore settings, IRepositoryCatalog catalog, ILayoutStore? preferences)
    {
        StatusBar = services.StatusBar;
        Layout = new(preferences, StatusBar);
        AutoFetch = new(Layout, StatusBar, services.Time);
        Accounts = new(catalog);
        Session = new(services, settings, Present, Cancels);
        Catalog = new(catalog, Accounts, services, Session, Cancels);
        Session.RecordOpened = Catalog.RecordOpenedAsync;
        Accounts.Edited += FollowAccounts;
    }

    public event Action? AccountsEdited;

    // What the shell's Cancel reaches that outlives a repository (the open in flight, the repository list, a clone); every
    // repository keeps its own (Repository.Cancels). Its Changed also says when the shown repository, its busy state or its
    // own registry changed.
    public CancelRegistry Cancels { get; } = new();
    public StatusBarState StatusBar { get; }
    public LayoutPreferences Layout { get; }
    public AutoFetchModel AutoFetch { get; }
    public AccountsModel Accounts { get; }
    public RepositorySession Session { get; }
    public CatalogModel Catalog { get; }

    // The shell's Cancel is offered while the shown repository is read or written, or while anything it reaches runs: the
    // application's operations and the shown repository's.
    public bool CanCancel => Session.Current.IsBusy || Cancels.CanCancel || Session.Current.Cancels.CanCancel;

    public void Cancel()
    {
        Cancels.CancelAll();
        Session.Current.Cancels.CancelAll();
    }

    // The application shows every repository the session starts from now on, the current one first.
    public void Attach(Action<Repository> show)
    {
        this.show = show;
        Present(Session.Current);
    }

    // The application starts: the saved layout is read, then, once what the repository needs is ready (a Git to run), the
    // last repository opens again; once. False when it had started already, or the application went before it was ready.
    // What the layout's read said stays after the open, which clears the status bar, in front of what the open said.
    public async Task<bool> StartAsync(CancellationToken ct, Func<CancellationToken, Task<bool>>? ready = null)
    {
        if (started) return false;
        started = true;
        await Layout.LoadLayoutAsync(ct);
        var layout = StatusBar.Notice;
        if (ready is not null && !await ready(ct)) return false;
        await Session.RestoreAsync(ct);
        if (StatusBar.Notice != layout) StatusBar.Notice = string.Join(" ", new[] { layout, StatusBar.Notice }.Where(notice => notice.Length != 0));
        return true;
    }

    // The application stays open while a Git write runs, and the status bar says why; false lets it close.
    public bool RefusesToClose()
    {
        if (!Session.Current.IsWriting) return false;
        StatusBar.Notice = ShellText.OperationRunning;
        return true;
    }

    // What has to end before the application goes: a layout save in flight.
    public Task Saving => Layout.SaveCompletion;

    // The application goes: the automatic fetch and the session stop, the shown repository is retired, and everything
    // Cancel reaches and the catalog's reads end. The features let go after this; DisposeAsync then releases
    // the workspace and waits for the layout save.
    public void Stop()
    {
        Accounts.Edited -= FollowAccounts;
        AutoFetch.Stop();
        Session.Stop();
        Cancel();
        Session.Current.Remotes.Clear();
        Catalog.CancelReads();
    }

    public async ValueTask DisposeAsync()
    {
        await Session.DisposeAsync();
        await Saving;
    }

    private void FollowAccounts()
    {
        if (Session.Current.Remotes.CanReread) Session.Current.Remotes.Reread();
        AccountsEdited?.Invoke();
    }

    private void Present(Repository repository)
    {
        if (show is null) return;
        if (shown is not null)
        {
            shown.Cancels.Changed -= Cancels.NotifyChanged;
            shown.PropertyChanged -= FollowBusy;
        }
        shown = repository;
        repository.Cancels.Changed += Cancels.NotifyChanged;
        repository.PropertyChanged += FollowBusy;
        show(repository);
        AutoFetch.Follow(repository);
        Cancels.NotifyChanged();
    }

    private void FollowBusy(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Repository.IsBusy)) Cancels.NotifyChanged();
    }
}
