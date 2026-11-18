using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Trag.Desktop.Diagnostics;
using Trag.Desktop.Diagnostics.Preview;
using Trag.Desktop.Diagnostics.Probe;
using Trag.Desktop.Platform;
using Trag.Application.Ports.Catalog;
using Trag.Desktop.Views;
using Trag.Infrastructure.Accounts;
using Trag.Infrastructure.Accounts.Providers;
using Trag.Infrastructure.Git.Catalog;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Platform.Secrets;
using Trag.Infrastructure.Platform.Storage;
using Trag.Infrastructure.Updates;
using Trag.ViewModels.Shell;

namespace Trag.Desktop.Composition;

// The composition root of a UI launch: the window, the account service, the catalog and the shell, the theme that follows
// the layout, the probe hooks, and the shutdown order (cancel the lifetime, dispose the shell, then the accounts).
public sealed class ShellComposition : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly ShellViewModel shell;
    private readonly IDisposable accounts;
    internal CancellationToken Lifetime => lifetime.Token;

    internal ShellComposition(MainWindow window, ShellViewModel shell, IDisposable accounts)
    {
        this.shell = shell; this.accounts = accounts;
        window.DataContext = shell;
        window.Closed += async (_, _) => await DisposeAsync();
    }

    public static void Start(IClassicDesktopStyleApplicationLifetime desktop, Avalonia.Application application, string localApplicationData)
    {
        var mode = ModeSelector.Select(desktop.Args ?? [], Environment.GetEnvironmentVariable);
        var paths = AppPaths.Resolve(mode, localApplicationData);
        var crashes = paths.CrashLogs is { } crashLogs ? new CrashLog(crashLogs, BuildVersion.Informational, TimeProvider.System) : null;
        if (crashes is not null) WatchCrashes(crashes);
        var window = new MainWindow();
        window.SettingsHost.CrashLogs = paths.CrashLogs;
        // The platform secret store for a normal launch, an in-memory one for probes.
        var accounts = new AccountService(paths.Accounts, mode.IsProbeMode ? new MemorySecretStore() : SecretStoreFactory.CreatePlatformStore(), new ProviderClient());
        window.SettingsHost.AccountService = accounts;
        // A probe drives the app without joining accounts to Git; the settings dialog still manages them.
        var gitAccounts = mode.IsProbeMode ? null : accounts;
        // One Git program for the shell and the catalog: "git" from the PATH until the start finds or the user chooses one.
        var git = new GitExecutable();
        var layout = paths.Layout is { } diff ? new LayoutStore(diff) : null;
        if (layout is not null) ThemeController.ShowSaved(application, layout);
        // A probe launch has nowhere to write its list, so it keeps one for as long as it runs.
        IRepositoryCatalogStore catalogStore = paths.Catalog is { } file ? new RepositoryCatalogStore(file) : new MemoryRepositoryCatalogStore();
        var catalog = new RepositoryCatalogFacade(new GitProcessRunner(executable: git), catalogStore, gitAccounts, Environment.ProcessPath);
        var shell = ShellFactory.Create(git, catalog, gitAccounts,
            paths.Settings is { } last ? new LastRepositoryStore(last) : null, layout,
            paths.Git is { } choice ? new GitChoiceStore(choice) : null, new ViewDispatcher(), crashes is null ? _ => { } : crashes.Record);
        ThemeController.Follow(application, shell.Layout);
        var composition = new ShellComposition(window, shell, accounts);
        desktop.MainWindow = window;
        if (!mode.IsProbeMode)
        {
            window.Opened += (_, _) => Dispatcher.UIThread.Post(async () => await shell.InitializeAsync(composition.Lifetime), DispatcherPriority.Background);
            // The automatic fetch runs on the UI thread's timeline; the probes drive the app without it.
            shell.Workbench.AutoFetch.Run();
        }
        window.SettingsHost.AutoFetch = shell.AutoFetch;
        window.SettingsHost.Git = shell.Git.Setup;
        // Finity Software's update service, asked only when the user checks; a probe asks no service.
        if (!mode.IsProbeMode) window.SettingsHost.Updates = new UpdateClient();
        if (mode.AttachUiProbe) UiProbeSession.Attach(window, shell, new SyntheticPreview(shell), code => desktop.Shutdown(code));
        if (mode.RunUiSmoke) UiSmokeRun.Schedule(window, desktop, mode.RunUiAuthSmoke);
    }

    // An error nothing caught is heard twice: by the dispatcher on the UI thread before it lets the error go, and by the
    // runtime, on any thread, as the process stops. The dispatcher's does not depend on the runtime raising its own.
    private static void WatchCrashes(CrashLog log)
    {
        Dispatcher.UIThread.UnhandledException += (_, e) => log.Record(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Record((Exception)e.ExceptionObject);
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        await shell.DisposeAsync();
        accounts.Dispose();
        lifetime.Dispose();
    }
}
