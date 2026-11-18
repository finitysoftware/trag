using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Trag.Desktop.Dialogs;
using Trag.Desktop.Views;
using Trag.Domain.Presentation;
using Trag.ViewModels.Shell;

namespace Trag.Desktop.Behaviors;

// Every keyboard shortcut of the main window in one table. The command gestures ride on the window's KeyBindings, so an
// open dialog's Escape wins over Cancel; the focus gestures tunnel ahead of the focused control, as a desktop app expects.
// While the focus is in an open dialog or flyout, none of them reaches what is behind it. The repository list hides the
// open repository: there F5 reads the list again, and the keys that act on the repository do nothing. Ctrl+Enter commits
// only while the file status section shows the message it commits.
public static class ShortcutMap
{
    private static readonly (string Gesture, Func<ShellViewModel, ICommand?> Command)[] Commands =
    [
        ("Ctrl+O", shell => shell.OpenCommand),
        ("F5", shell => OnList(shell) ? shell.Catalog.RefreshCatalogCommand : shell.RefreshCommand),
        ("Ctrl+W", shell => Workspace(shell, shell.CloseRepositoryCommand)),
        ("Ctrl+Shift+F", shell => Workspace(shell, shell.Sync.FetchCommand)),
        ("Ctrl+Shift+L", shell => Workspace(shell, shell.Sync.PullCommand)),
        ("Ctrl+Shift+P", shell => Workspace(shell, shell.Sync.PushCommand)),
        ("Ctrl+Shift+M", shell => Workspace(shell, shell.MergeDialog.OpenCommand)),
        ("Ctrl+Enter", shell => shell.Navigator.Section == WorkspaceSection.FileStatus ? shell.Commit.CommitCommand : null),
        ("Ctrl+Shift+K", shell => Workspace(shell, shell.Stash.OpenStashCommand)),
        ("Escape", shell => shell.CancelCommand),
        ("Ctrl+OemComma", shell => shell.Catalog.RequestSettingsCommand),
    ];

    private static bool OnList(ShellViewModel shell) => shell.Navigator.Section == WorkspaceSection.Repositories;
    private static ICommand? Workspace(ShellViewModel shell, ICommand command) => OnList(shell) ? null : command;

    public static void Install(MainWindow window)
    {
        ShellViewModel? Shell() => window.DataContext as ShellViewModel;
        foreach (var (gesture, command) in Commands)
            window.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse(gesture), Command = new Forwarding(() => !Covered(window) && Shell() is { } shell ? command(shell) : null) });
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => Tunnel(window, e, Shell()), RoutingStrategies.Tunnel);
    }

    // Ctrl+K the commit message; Ctrl+1 / Ctrl+2 the file status or history list; Ctrl+F / Ctrl+3 the search box.
    private static void Tunnel(MainWindow window, KeyEventArgs e, ShellViewModel? model)
    {
        if (model is null || e.KeyModifiers != KeyModifiers.Control || Covered(window)) return;
        if (e.Key == Key.K && model.PrepareCommitCommand.CanExecute(null))
        {
            model.PrepareCommitCommand.Execute(null);
            Dispatcher.UIThread.Post(window.FocusCommitEditor);
            e.Handled = true;
        }
        else if (e.Key is Key.D1 or Key.D2)
        {
            if (e.Key == Key.D1) model.Navigator.ShowFileStatusCommand.Execute(null); else model.Navigator.ShowHistoryCommand.Execute(null);
            Dispatcher.UIThread.Post(() => { if (model.Navigator.Section == WorkspaceSection.FileStatus) window.FocusFileList(); else window.FocusHistoryList(); });
            e.Handled = true;
        }
        else if (e.Key is Key.F or Key.D3)
        {
            model.Navigator.ShowSearchCommand.Execute(null);
            Dispatcher.UIThread.Post(window.FocusHistorySearch);
            e.Handled = true;
        }
    }

    // A flyout's content lies under an overlay popup host when popups are drawn in the window, as in headless.
    private static bool Covered(MainWindow window) =>
        window.FocusManager?.GetFocusedElement() is Visual focused
        && focused.GetSelfAndVisualAncestors().Any(visual => visual is DialogHost or OverlayPopupHost);

    // A key binding's command that follows the shell of the moment; availability is asked at the key press.
    private sealed class Forwarding(Func<ICommand?> target) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => target()?.CanExecute(parameter) ?? false;
        public void Execute(object? parameter) => target()?.Execute(parameter);
    }
}
