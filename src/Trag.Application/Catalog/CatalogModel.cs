using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Accounts;
using Trag.Application.Live;
using Trag.Application.Operations;
using Trag.Application.Ports.Catalog;
using Trag.Application.Ports.Threading;
using Trag.Domain.Accounts;
using Trag.Domain.Catalog;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Application.Catalog;

// The repositories Trag knows beyond the one open: the local list and its links, the accounts with their hosted
// repositories, and clone. The catalog port is split here into what the local list and clone take; the accounts and
// what they host are the accounts model's. The catalog's read fills the picker as it goes: Accounts when the accounts
// were read, LinksChecked when every link was checked and the hosted repositories match their clones again. Adding,
// initializing, removing and opening a listed repository and cloning a hosted one go through here too, and Message says
// what each came to.
public sealed partial class CatalogModel : ObservableObject
{
    private readonly AccountsModel accounts;
    private readonly CancelRegistry cancels;
    private readonly StatusBarState statusBar;
    private readonly IViewDispatcher dispatcher;
    private readonly RepositorySession session;

    // The application's services give the catalog the status bar and the view's thread, the workbench its Cancel registry.
    public CatalogModel(IRepositoryCatalog catalog, AccountsModel accounts, RepositoryServices services, RepositorySession session, CancelRegistry cancels)
    {
        this.accounts = accounts;
        this.cancels = cancels;
        statusBar = services.StatusBar;
        dispatcher = services.Dispatcher;
        this.session = session;
        var list = new LocalRepositoryCatalog(catalog, catalog, catalog);
        Local = new(list, statusBar);
        Clone = new(catalog, list);
        Read = new(empty: [], cancels) { FailedMessage = CatalogText.ListNotRead };
        Read.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Read.IsBusy)) OnPropertyChanged(nameof(IsBusy));
            else if (e.PropertyName == nameof(Read.Message) && Read.Message.Length != 0) Message = Read.Message;
        };
        Local.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(LocalRepositoriesModel.IsInitializing)) OnPropertyChanged(nameof(IsBusy)); };
        // The shell's Cancel reaches a running clone.
        cancels.Register(() => Cloning.IsBusy, Cloning.Cancel);
        Cloning.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Operation.IsBusy)) cancels.NotifyChanged(); };
    }

    // What the picker says: its welcome, then what the last read, addition, removal, open or clone came to, or what the
    // window answered about a folder or a page.
    [ObservableProperty] public partial string Message { get; set; } = CatalogText.Welcome;
    // Busy while the list is read or a folder is being initialized, whichever ends first.
    public bool IsBusy => Read.IsBusy || Local.IsInitializing;
    // A clone, one at a time.
    public Operation Cloning { get; } = new();
    public LocalRepositoriesModel Local { get; }
    public CloneWorkflow Clone { get; }
    // The read's activity, failure and completion; its value is the accounts it read.
    public Loader<IReadOnlyList<HostedRepositories>> Read { get; }
    // One item per account, kept for as long as the account stays, in display order.
    public IReadOnlyList<HostedRepositories> Accounts { get; private set; } = [];
    public event Action? LinksChecked;

    public Task ReadAsync() => Read.LoadAsync(ReadCatalogAsync);

    // Everything the catalog reads stops: the list read and every account's hosted list.
    public void CancelReads()
    {
        Read.Cancel();
        foreach (var account in Accounts)
            account.Cancel();
    }

    // The list, the accounts and every local link, in that order and shown as each arrives; a list or accounts that cannot
    // be read stay as they were and say so, the rest is read all the same.
    private async Task<IReadOnlyList<HostedRepositories>?> ReadCatalogAsync(CancellationToken ct)
    {
        var listed = await TryLoadListAsync(ct);
        ct.ThrowIfCancellationRequested();
        Local.Rebuild();
        var read = await accounts.TryListAsync(ct);
        ct.ThrowIfCancellationRequested();
        if (read is not null) Follow(read);
        if (!listed) Message = read is null ? CatalogText.ListAndAccountsNotRead : CatalogText.ListNotRead;
        else if (read is null) Message = CatalogText.AccountsNotRead;
        await Local.ProbeAsync(() => !ct.IsCancellationRequested, ct);
        ct.ThrowIfCancellationRequested();
        LinksChecked?.Invoke();
        return Accounts;
    }

    // A cancelled load is a list not read too; the read's cancellation check right after ends the read.
    private async Task<bool> TryLoadListAsync(CancellationToken ct)
    {
        try { await Local.EnsureLoadedAsync(ct); return true; }
        catch (Exception) { return false; }
    }

    // An account read again keeps its hosted list; an account that went stops reading it and leaves the shell's Cancel.
    private void Follow(IReadOnlyList<AccountInfo> read)
    {
        var existing = Accounts.ToDictionary(hosted => hosted.Account.Id, StringComparer.Ordinal);
        Accounts = read.Select(account => existing.TryGetValue(account.Id, out var hosted) ? hosted : new(accounts, account, cancels)).ToArray();
        foreach (var removed in existing.Values.Where(hosted => !Accounts.Contains(hosted)))
            removed.Release();
        OnPropertyChanged(nameof(Accounts));
    }

    // A listed repository opens while no repository is being read or written and no clone runs.
    public bool CanOpen => !session.Current.IsBusy && !Cloning.IsBusy;

    // Opens a listed repository; false when it did not, with why in the message: what the link check found, or what the
    // status bar says about the open. A start refused while something runs says nothing.
    public async Task<bool> OpenAsync(LocalRepository item)
    {
        if (!CanOpen) return false;
        if (!item.IsValid)
        {
            Message = CatalogText.Link(item.Status, item.IsChecking);
            return false;
        }
        await session.OpenRepositoryAsync(item.Path);
        if (session.Current.HasRepository) return true;
        Message = statusBar.ErrorMessage.Length != 0 ? statusBar.ErrorMessage : statusBar.Status;
        return false;
    }

    // A folder the picker chose joins the list or is found there; the message says which, or why not.
    public async Task<CatalogAddition> AddAsync(string path) => Say(await Local.AddAsync(path), CatalogText.AddFailed);

    // Git makes the folder a repository, which then joins the list like any other; the message says how that went, a
    // cancellation as a failure.
    public async Task<CatalogAddition> InitializeAsync(string path)
    {
        var added = await Local.InitializeAsync(path);
        Message = added.Entry is { } entry ? CatalogText.Initialized(entry.Name)
            : CatalogText.Addition(added.Outcome, CatalogText.InitializeFailed, "") ?? CatalogText.InitializeFailed;
        return added;
    }

    // The entry leaves the list at once; a list that could not be saved is said, but does not bring it back.
    public async Task RemoveAsync(LocalRepository item)
    {
        var saved = Local.RemoveAsync(item);
        Message = CatalogText.Removed(item.Name);
        if (!await saved) Message = CatalogText.RemovedNotSaved;
    }

    // What became of a folder offered to the list, in the picker's words; failed is what a failure means there.
    private CatalogAddition Say(CatalogAddition added, string failed)
    {
        if (CatalogText.Addition(added.Outcome, failed, added.Entry?.Name ?? "") is { } said) Message = said;
        return added;
    }

    // A hosted repository cloned into the target: the status bar follows it, progress hears every step on the view's
    // thread while the clone runs, and a clone that joined the list comes back with its addition, which the message names.
    public Task<CloneResult?> CloneAsync(RemoteRepository repository, AccountInfo account, string target, bool submodules, Action<string> progress) =>
        Cloning.RunAsync<CloneResult>(async ct => await CloneCoreAsync(repository, account, target, submodules, progress, ct));

    private async Task<CloneResult> CloneCoreAsync(RemoteRepository repository, AccountInfo account, string target, bool submodules, Action<string> progress, CancellationToken ct)
    {
        statusBar.Status = CatalogText.CloningFrom(repository.FullName);
        var running = true;
        try
        {
            var outcome = await Clone.RunAsync(new(repository.CloneUrl, target, account.Id, ProgressDelivery.Of<string>(value => dispatcher.InvokeAsync(() =>
            {
                if (!running) return;
                statusBar.Status = value;
                progress(value);
            })), submodules), ct);
            statusBar.Status = outcome.Succeeded ? CatalogText.Cloned : outcome.Message;
            if (outcome.Succeeded) Message = outcome.Addition?.Entry is { } entry ? CatalogText.ClonedAs(entry.Name) : CatalogText.Cloned;
            return outcome;
        }
        finally { running = false; }
    }

    // The session opened a repository: the list records it.
    public Task RecordOpenedAsync(RepositorySnapshot snapshot, CancellationToken ct) => Local.RecordOpenedAsync(snapshot, ct);
}
