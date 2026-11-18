using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Trag.Application.Live;
using Trag.Application.Ports.Accounts;
using Trag.ViewModels.Accounts;
using Trag.ViewModels.Setup;
using Trag.ViewModels.Sync;

namespace Trag.Desktop.Dialogs.Settings;

public sealed partial class SettingsWindow : Window
{
    public AccountsViewModel Model { get; } = null!;
    public Task Initialization { get; private set; } = Task.CompletedTask;
    public SettingsWindow() => InitializeComponent();
    public SettingsWindow(AccountsViewModel model, IAccountService service, AutoFetchViewModel? autoFetch = null, LayoutPreferences? layout = null,
        GitSettingsViewModel? git = null) : this()
    {
        Git.DataContext = git;
        GitSection.IsVisible = git is not null;
        AutoFetch.DataContext = autoFetch;
        AutoFetch.IsVisible = autoFetch is not null;
        Appearance.DataContext = layout;
        AppearanceSection.IsVisible = layout is not null;
        Model = model;
        DataContext = Model;
        GitLabEntryOverlay.Service = service;
        Opened += (_, _) => Initialization = Task.WhenAll(Model.LoadAsync(), git?.LoadAsync() ?? Task.CompletedTask);
        Closed += (_, _) => { Model.Close(); GitLabEntryOverlay.ClearToken(); git?.Detach(); };
    }
    private void Done(object? sender, RoutedEventArgs e) => Close();
    // Escape closes as Done does. OnKeyDown sees only a key nothing under Settings has taken, so a dialog over the
    // cards or an open list gets Escape first.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Close();
    }
}
