using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Trag.Desktop.Behaviors;
using Trag.Desktop.Platform;
using Trag.Desktop.Services;
using Trag.Desktop.Views.History;
using Trag.Desktop.Views.Merge;
using Trag.Desktop.Views.Rebase;

namespace Trag.Desktop.Views;

public sealed partial class MainWindow : Window
{
    public SettingsWindowHost SettingsHost { get; }
    public MainWindow()
    {
        InitializeComponent();
        WindowChrome.Apply(this);
        var launcher = new ShellLauncher(Process.Start);
        SettingsHost = new SettingsWindowHost(this, launcher);
        // The host lives as long as the window whose shell it serves: the window's events hold it.
        _ = new ShellHost(this, SettingsHost, launcher);
        ShortcutMap.Install(this);
        // Coming back to the window fetches what others pushed meanwhile, when the last fetch is a while ago, and looks for
        // Git again while there is none to run, as after installing it.
        Activated += (_, _) =>
        {
            if (DataContext is not ViewModels.Shell.ShellViewModel shell) return;
            _ = shell.Workbench.AutoFetch.ActivatedAsync();
            _ = shell.Git.ActivatedAsync();
        };
    }

    private HistoryView? History => this.GetVisualDescendants().OfType<HistoryView>().FirstOrDefault();
    // What the shell host and the shortcut table ask of the window, so neither looks a control up by name.
    // Settings opens from More, so focus goes back there when it closes.
    public void FocusMore() => ActionsBar.MoreButton.Focus();
    // The message editor of the merge or rebase view while one shows, else the commit message editor.
    public void FocusCommitEditor() => ((Control?)this.GetVisualDescendants().OfType<MergeView>().FirstOrDefault()?.MergeMessageEditor
        ?? (Control?)this.GetVisualDescendants().OfType<RebaseView>().FirstOrDefault()?.RebaseMessageEditor ?? Workspace.Composer.CommitMessageEditor).Focus();
    // The list of the shown section when it is visible, else the window itself.
    public void FocusFileList() { var list = Workspace.Unstaged.UnstagedList; if (list.IsEffectivelyVisible) list.Focus(); else Focus(); }
    public void FocusHistoryList() { if (History?.Commits.HistoryList is { IsEffectivelyVisible: true } list) list.Focus(); else Focus(); }
    public void FocusHistorySearch() => History?.Commits.HistorySearchEditor.Focus();
}
