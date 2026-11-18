using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Trag.Desktop.Composition;
using Trag.Desktop.Themes;
using Trag.Desktop.Views;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Platform.Storage;
using Trag.Infrastructure.Updates;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.Desktop.UnitTests.Composition;

public sealed class ShellCompositionTests
{
    // The launch a user makes: files under the local application data, the last repository opened once the window has,
    // and the automatic fetch bringing in what another clone pushed meanwhile, checked in Git itself.
    [AvaloniaFact]
    public async Task WhenStartRunsWithRealPathsOutsideProbeMode_ShouldInitializeRestoreAndStartAutoFetch()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await fixture.CommitAsync("first");
        var remote = Path.Combine(fixture.Container, "remote.git");
        await fixture.GitAtAsync(fixture.Container, "init", "--bare", "--initial-branch=main", remote);
        await fixture.GitAsync("remote", "add", "origin", remote);
        await fixture.GitAsync("push", "--set-upstream", "origin", "main");
        var other = Path.Combine(fixture.Container, "other");
        await fixture.GitAtAsync(fixture.Container, "clone", remote, other);
        await fixture.GitAtAsync(other, "-c", "user.name=Other", "-c", "user.email=other@example.invalid", "-c", "commit.gpgsign=false",
            "commit", "--allow-empty", "-m", "pushed meanwhile");
        await fixture.GitAtAsync(other, "push");
        var pushed = await fixture.GitAtAsync(other, "rev-parse", "HEAD");
        var data = Path.Combine(fixture.Container, "local app data");
        await new LastRepositoryStore(Path.Combine(data, "Trag", "settings.json")).SaveAsync(fixture.Root, CancellationToken.None);
        var desktop = new ClassicDesktopStyleApplicationLifetime { Args = [] };

        ShellComposition.Start(desktop, Avalonia.Application.Current!, data);

        var window = Assert.IsType<MainWindow>(desktop.MainWindow);
        var shell = Assert.IsType<ShellViewModel>(window.DataContext);
        try
        {
            window.Show();
            Assert.IsType<UpdateClient>(window.SettingsHost.Updates);
            await AppHarness.Until(() => shell.Repository.Snapshot is { } shown && Path.GetFullPath(shown.Root) == fixture.Root, "the last repository to open");
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (await fixture.GitAsync("rev-parse", "origin/main") != pushed)
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Timed out waiting for the automatic fetch");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                await Task.Delay(100);
            }
        }
        finally
        {
            var repository = shell.Repository;
            window.Close();
            await AppHarness.Until(() => repository.IsRetired, "the closed window to stop the shell");
        }
    }

    // Three settings files unreadable at one launch that restores a repository with an upstream: the banner says the
    // layout's and the list's sentences, and the sync panel says the accounts' instead of an unexplained "unknown".
    [AvaloniaFact]
    public async Task WhenTheAccountsTheListAndTheLayoutAreCorruptAndTheLastRepositoryIsRestored_ShouldShowAllThreeNotices()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await fixture.CommitAsync("first");
        var remote = Path.Combine(fixture.Container, "remote.git");
        await fixture.GitAtAsync(fixture.Container, "init", "--bare", "--initial-branch=main", remote);
        await fixture.GitAsync("remote", "add", "origin", remote);
        await fixture.GitAsync("push", "--set-upstream", "origin", "main");
        var data = Path.Combine(fixture.Container, "local app data");
        var settings = Path.Combine(data, "Trag", "settings.json");
        await new LastRepositoryStore(settings).SaveAsync(fixture.Root, CancellationToken.None);
        foreach (var corrupt in new[] { "accounts.json", "repositories.json", "settings.json.diff.json" })
            await File.WriteAllTextAsync(Path.Combine(data, "Trag", corrupt), "{");
        var desktop = new ClassicDesktopStyleApplicationLifetime { Args = [] };

        ShellComposition.Start(desktop, Avalonia.Application.Current!, data);

        var window = Assert.IsType<MainWindow>(desktop.MainWindow);
        var shell = Assert.IsType<ShellViewModel>(window.DataContext);
        try
        {
            window.Show();
            await AppHarness.Until(() => shell.Repository.Snapshot is { } shown && Path.GetFullPath(shown.Root) == fixture.Root, "the last repository to open");
            await AppHarness.Until(() => shell.Sync.Panel.Message == AccountFailureText.SettingsNotRead, "the sync read to fail on the accounts");
            AppHarness.Layout(window);

            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.IsEffectivelyVisible
                && block.Text == LayoutText.NotLoaded + " " + CatalogText.OpenedNotRecorded);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.IsEffectivelyVisible
                && block.Text == AccountFailureText.SettingsNotRead);
        }
        finally
        {
            var repository = shell.Repository;
            window.Close();
            await AppHarness.Until(() => repository.IsRetired, "the closed window to stop the shell");
        }
    }

    // The first frame is drawn in the saved theme, not in Windows' own until the layout is read after the window opened.
    [AvaloniaFact]
    public async Task WhenTheSavedLayoutHasADarkTheme_ShouldApplyItBeforeTheWindowOpens()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var data = Path.Combine(fixture.Container, "local app data");
        await new LayoutStore(Path.Combine(data, "Trag", "settings.json.diff.json"))
            .SaveLayoutAsync(new SavedLayout(Theme: (int)ThemeChoice.DarkVantablack), CancellationToken.None);
        var desktop = new ClassicDesktopStyleApplicationLifetime { Args = [] };
        var application = Avalonia.Application.Current!;

        ShellComposition.Start(desktop, application, data);

        var window = Assert.IsType<MainWindow>(desktop.MainWindow);
        var shell = Assert.IsType<ShellViewModel>(window.DataContext);
        try
        {
            Assert.Same(AppThemes.DarkVantablack, application.RequestedThemeVariant);
        }
        finally
        {
            window.Show();
            await AppHarness.Until(() => shell.Layout.Theme == ThemeChoice.DarkVantablack, "the layout to be read after the window opened");
            var repository = shell.Repository;
            window.Close();
            await AppHarness.Until(() => repository.IsRetired, "the closed window to stop the shell");
            application.RequestedThemeVariant = ThemeVariant.Default;
        }
    }

    [AvaloniaFact]
    public async Task WhenTheCompositionIsDisposed_ShouldCancelTheLifetimeAndDisposeTheShellBeforeTheAccounts()
    {
        var h = new AppHarness();
        var shell = h.Shell();
        var window = new MainWindow();
        var accounts = new RecordingDisposable();
        var composition = new ShellComposition(window, shell, accounts);
        Assert.Same(shell, window.DataContext); Assert.False(composition.Lifetime.IsCancellationRequested);
        shell.Layout.DiffMode = 1; // a pending layout save the shell waits for before it is gone
        Assert.False(shell.Layout.SaveCompletion.IsCompleted);
        accounts.OnDispose = () =>
        {
            Assert.True(composition.Lifetime.IsCancellationRequested, "the lifetime is cancelled before the accounts go");
            Assert.True(shell.Layout.SaveCompletion.IsCompleted, "the shell is disposed before the accounts go");
        };
        await composition.DisposeAsync();
        Assert.True(accounts.Disposed); Assert.Equal(1, h.Preferences.Saves);
    }

    [AvaloniaFact]
    public async Task WhenTheWindowCloses_ShouldDisposeTheComposition()
    {
        var h = new AppHarness();
        var shell = h.Shell();
        var window = new MainWindow();
        var accounts = new RecordingDisposable();
        _ = new ShellComposition(window, shell, accounts);
        window.Show(); AppHarness.Layout(window);
        window.Close();
        await AppHarness.Until(() => accounts.Disposed, "the composition to dispose after Closed");
    }

    private sealed class RecordingDisposable : IDisposable
    {
        public bool Disposed { get; private set; }
        public Action? OnDispose { get; set; }
        public void Dispose() { OnDispose?.Invoke(); Disposed = true; }
    }
}
