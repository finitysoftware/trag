using Trag.Domain.Presentation;
using Trag.Domain.Publish;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Trag.Desktop.Platform;
using Trag.Desktop.Services;
using Trag.TestSupport;
using Trag.Desktop.UnitTests.Dialogs.Settings;

namespace Trag.Desktop.UnitTests.Views;

// The glue between a window and its shell: ShellHost, SettingsWindowHost and the settings window, exercised through real windows.
public sealed class WindowHostTests
{
    [AvaloniaFact]
    public async Task WhenTheDataContextIsSwapped_ShouldStopReactingToTheOldModel()
    {
        var h = new AppHarness();
        await using var first = h.Shell(); await using var second = h.Shell();
        var window = h.Window(first);
        window.DataContext = second; AppHarness.Layout(window);
        first.Catalog.RequestSettingsCommand.Execute(null); AppHarness.Layout(window);
        Assert.Null(window.SettingsHost.ActiveSettings);
        second.Catalog.RequestSettingsCommand.Execute(null);
        await AppHarness.Until(() => window.SettingsHost.ActiveSettings is not null, "settings from the live model");
        window.SettingsHost.ActiveSettings!.Close(); AppHarness.Layout(window);
        Assert.Null(window.SettingsHost.ActiveSettings);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheWindowIsClosed_ShouldStopReactingToItsModelsRequests()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        window.Close();
        model.Catalog.RequestSettingsCommand.Execute(null);
        AppHarness.Layout(window);
        Assert.Null(window.SettingsHost.ActiveSettings);
    }

    [AvaloniaFact]
    public async Task WhenSettingsCloseInOneOfTwoWindows_ShouldRereadOnlyItsOwnShellAndNothingOnceThatShellIsDisposed()
    {
        var first = new AppHarness(); first.Reader.Snapshot = first.Synced();
        var second = new AppHarness(); second.Reader.Snapshot = second.Synced();
        var a = first.Shell(); await using var b = second.Shell();
        var windowA = first.Window(a); var windowB = second.Window(b);
        await a.Session.OpenRepositoryAsync("fixture"); await a.Sync.SyncCompletion;
        await b.Session.OpenRepositoryAsync("fixture"); await b.Sync.SyncCompletion;
        var aReads = first.Sync.Reads.Count; var bReads = second.Sync.Reads.Count;
        Assert.NotSame(a.Workbench, b.Workbench);
        a.Catalog.RequestSettingsCommand.Execute(null);
        await AppHarness.Until(() => windowA.SettingsHost.ActiveSettings is not null, "settings in the first window");
        Assert.Null(windowB.SettingsHost.ActiveSettings);
        windowA.SettingsHost.ActiveSettings!.Close(); AppHarness.Layout(windowA);
        await a.Sync.SyncCompletion;
        Assert.Equal(aReads + 1, first.Sync.Reads.Count); Assert.Equal(bReads, second.Sync.Reads.Count);
        await a.DisposeAsync();
        a.Catalog.RequestSettingsCommand.Execute(null);
        await AppHarness.Until(() => windowA.SettingsHost.ActiveSettings is not null, "settings after the shell was disposed");
        windowA.SettingsHost.ActiveSettings!.Close(); AppHarness.Layout(windowA);
        await a.Sync.SyncCompletion;
        Assert.Equal(aReads + 1, first.Sync.Reads.Count); Assert.Equal(bReads, second.Sync.Reads.Count);
        windowA.Close(); windowB.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheWindowClosesDuringALayoutSave_ShouldWaitForTheSaveThenClose()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        model.Layout.DiffMode = 1;
        Assert.False(model.Layout.SaveCompletion.IsCompleted);
        window.Close(); AppHarness.Layout(window);
        Assert.True(window.IsVisible);
        await AppHarness.Until(() => !window.IsVisible, "the window to close after the save");
        Assert.Equal(1, h.Preferences.Saves); Assert.Equal(1, h.Preferences.Saved!.Mode);
    }

    // A folder or a file that is there but that no terminal, file manager, program or browser could take: the status bar
    // says which did not start, and the folder or the file is not called gone; what starts is not mentioned.
    [AvaloniaFact]
    public async Task WhenTheSystemCannotStartWhatTheShellAsksFor_ShouldSaySoInTheStatusBar()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        var folder = Directory.CreateTempSubdirectory("trag-shell-host-").FullName; var file = Path.Combine(folder, "a.txt");
        await File.WriteAllTextAsync(file, "a");
        try
        {
            var started = new ShellHost(window, window.SettingsHost, new ShellLauncher(_ => null));
            started.OpenTerminal(folder); started.Reveal(folder); started.RevealFile(file); started.OpenFile(file); started.OpenBrowser(new Uri("https://example.com/"));
            Assert.Equal("", model.StatusBar.Notice);
            var host = new ShellHost(window, window.SettingsHost, new ShellLauncher(_ => throw new Win32Exception(2)));
            host.OpenTerminal(folder);
            Assert.Equal("No terminal could be started.", model.StatusBar.Notice);
            model.StatusBar.Notice = "";
            Assert.True(host.Reveal(folder));
            Assert.Equal("The file manager could not be started.", model.StatusBar.Notice);
            model.StatusBar.Notice = "";
            Assert.True(host.RevealFile(file));
            Assert.Equal("The file manager could not be started.", model.StatusBar.Notice);
            Assert.True(host.OpenFile(file));
            Assert.Equal("No program could be started to open this file.", model.StatusBar.Notice);
            host.OpenBrowser(new Uri("https://example.com/"));
            Assert.Equal("The browser could not be started.", model.StatusBar.Notice);
        }
        finally { Directory.Delete(folder, true); }
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenThePublishDialogOpensInEachMode_ShouldFocusThatModesFirstField()
    {
        var h = new AppHarness(); h.Catalog.Accounts.Add(FakeAccountService.GitHub());
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; AppHarness.Layout(window);
        Assert.True(model.Publish.Context.ShowPublish);
        await model.Publish.OpenPublishCommand.ExecuteAsync(null); AppHarness.Layout(window);
        Assert.True(model.Publish.Draft.IsOpen); Assert.Equal(PublishKind.CreateRepository, model.Publish.Draft.Mode);
        Assert.Equal("PublishNameEditor", (window.FocusManager!.GetFocusedElement() as Control)?.Name);
        model.Publish.UsePublishUrlCommand.Execute(null); AppHarness.Layout(window);
        Assert.Equal("PublishUrlEditor", (window.FocusManager.GetFocusedElement() as Control)?.Name);
        model.Publish.CancelPublishCommand.Execute(null); AppHarness.Layout(window);
        Assert.False(model.Publish.Draft.IsOpen);
        h.Reader.Snapshot = Snapshots.Create(remotes: [Snapshots.Remote()]); // a remote without an upstream: branch mode
        await model.RefreshCommand.ExecuteAsync(null); await model.Sync.SyncCompletion; AppHarness.Layout(window);
        await model.Publish.OpenPublishCommand.ExecuteAsync(null); AppHarness.Layout(window);
        Assert.Equal(PublishKind.ExistingRemote, model.Publish.Draft.Mode);
        Assert.Equal("PublishRemoteSelector", (window.FocusManager.GetFocusedElement() as Control)?.Name);
        model.Publish.CancelPublishCommand.Execute(null); AppHarness.Layout(window);
        window.Close();
    }

    // The theme is chosen in Settings, for the shell the window shows.
    [AvaloniaFact]
    public async Task WhenTheThemeSelectorInSettingsOrThemeIndexChanges_ShouldKeepTheOtherInSyncAndSaveIt()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        model.Catalog.RequestSettingsCommand.Execute(null);
        await AppHarness.Until(() => window.SettingsHost.ActiveSettings is not null, "settings");
        var settings = window.SettingsHost.ActiveSettings!; AppHarness.Layout(settings);
        SettingsHarness.Choose(settings, "AppearanceSection");
        var selector = settings.Descendant<ComboBox>("ThemeSelector")!;
        Assert.True(selector.IsEffectivelyVisible);
        Assert.Equal(0, selector.SelectedIndex);
        selector.SelectedIndex = 2; AppHarness.Layout(settings);
        Assert.Equal(2, model.Layout.ThemeIndex);
        model.Layout.ThemeIndex = 1; AppHarness.Layout(settings);
        Assert.Equal(1, selector.SelectedIndex);
        await model.Layout.SaveCompletion;
        Assert.Equal(1, h.Preferences.Saved!.Theme);
        settings.Close(); window.Close();
    }

    // Settings names the themes by their short names; Graphite comes after the three themes Trag had, Obsidian and
    // Vantablack after it.
    [AvaloniaFact]
    public async Task WhenGraphiteIsChosenInSettings_ShouldBecomeTheLayoutsTheme()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        model.Catalog.RequestSettingsCommand.Execute(null);
        await AppHarness.Until(() => window.SettingsHost.ActiveSettings is not null, "settings");
        var settings = window.SettingsHost.ActiveSettings!; AppHarness.Layout(settings);
        SettingsHarness.Choose(settings, "AppearanceSection");
        var selector = settings.Descendant<ComboBox>("ThemeSelector")!;

        selector.SelectedIndex = 3; AppHarness.Layout(settings);

        Assert.Equal(("Graphite", ThemeChoice.DarkGraphite), (((ComboBoxItem)selector.SelectedItem!).Content, model.Layout.Theme));
        Assert.Equal(["System theme", "White", "Espresso", "Graphite", "Obsidian", "Vantablack"], selector.Items.Cast<ComboBoxItem>().Select(item => item.Content));
        await model.Layout.SaveCompletion;
        settings.Close(); window.Close();
    }

    [AvaloniaFact]
    public async Task WhenSettingsClose_ShouldRereadTheSyncStateOrRefreshTheRepositoryList()
    {
        var h = new AppHarness(); h.Reader.Snapshot = h.Synced();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; AppHarness.Layout(window);
        var reads = h.Sync.Reads.Count;
        model.Catalog.RequestSettingsCommand.Execute(null);
        await AppHarness.Until(() => window.SettingsHost.ActiveSettings is not null, "settings");
        Assert.Same(model, window.DataContext);
        window.SettingsHost.ActiveSettings!.Close(); AppHarness.Layout(window);
        await model.Sync.SyncCompletion;
        Assert.Equal(reads + 1, h.Sync.Reads.Count);
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion; AppHarness.Layout(window);
        var readsBeforeSettings = h.Catalog.AccountsReads;
        model.Catalog.RequestSettingsCommand.Execute(null);
        await AppHarness.Until(() => window.SettingsHost.ActiveSettings is not null, "settings again");
        window.SettingsHost.ActiveSettings!.Close(); AppHarness.Layout(window);
        await model.Catalog.CatalogCompletion;
        Assert.Equal(readsBeforeSettings + 1, h.Catalog.AccountsReads);
        Assert.Equal("MoreButton", (window.FocusManager!.GetFocusedElement() as Control)?.Name);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenAGitLabTokenIsConnected_ShouldSendItOnceClearTheEditorAndZeroTheBytes()
    {
        var service = new FakeAccountService();
        byte[]? copy = null; byte[]? backing = null; string? address = null; bool? memoryOnly = null;
        service.SignInGitLab = (instance, token, memory, _) =>
        {
            address = instance; memoryOnly = memory; copy = token.ToArray();
            backing = MemoryMarshal.TryGetArray(token, out var segment) ? segment.Array : null;
            var account = FakeAccountService.GitLab(host: instance.TrimEnd('/') + "/");
            service.Accounts.Add(account);
            return Task.FromResult(account);
        };
        var window = SettingsHarness.Window(service); window.Show(); await window.Initialization; AppHarness.Layout(window);
        window.Model.AddGitLabCommand.Execute(null); AppHarness.Layout(window);
        Assert.True(window.Model.ShowGitLabEntry);
        window.Descendant<TextBox>("GitLabTokenEditor")!.Text = "glpat-synthetic"; window.Model.MemoryOnly = true;
        window.Descendant<Button>("ConnectGitLabButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await AppHarness.Until(() => !window.Model.IsBusy && copy is not null, "the GitLab sign-in");
        Assert.Equal("glpat-synthetic"u8.ToArray(), copy);
        Assert.Equal("https://gitlab.com", address); Assert.True(memoryOnly);
        Assert.Equal("", window.Descendant<TextBox>("GitLabTokenEditor")!.Text ?? "");
        Assert.NotNull(backing); Assert.All(backing!, b => Assert.Equal(0, b));
        Assert.False(window.Model.ShowGitLabEntry); Assert.Contains("Connected as bob", window.Model.Status);
        Assert.Single(window.Model.GitLabAccounts);
        window.Close();
    }
}
