using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Ports.Storage;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;

namespace Trag.Application.Live;

// The saved layout as an observable model: the diff mode, the five splitter ratios, the theme, the automatic fetch interval
// (in minutes) and the height of a commit's message in History. Every change is written after a short pause,
// and writes are serialized so an older layout can never overtake a newer one.
public sealed partial class LayoutPreferences(ILayoutStore? store, StatusBarState statusBar) : ObservableObject
{
    private bool applying;
    private long revision;
    private Task save = Task.CompletedTask;
    [ObservableProperty]
    public partial int DiffMode { get; set; }
    [ObservableProperty] public partial double DiffSplitRatio { get; set; } = 0.5;
    [ObservableProperty] public partial double DiffFilesRatio { get; set; } = 0.36;
    // The theme's place in Settings' list, which the list binds; Theme says which theme it is.
    [ObservableProperty]
    public partial int ThemeIndex { get; set; }
    public ThemeChoice Theme => (ThemeChoice)ThemeIndex;
    [ObservableProperty] public partial double HistoryPanelRatio { get; set; } = 0.45;
    [ObservableProperty] public partial double StagePanelRatio { get; set; } = 0.5;
    [ObservableProperty] public partial double CommitPanelRatio { get; set; } = 1;
    [ObservableProperty] public partial double CommitMessageHeight { get; set; } = -1;
    [ObservableProperty] public partial int AutoFetchMinutes { get; set; } = 1;
    public Task SaveCompletion => save;

    // Every property is part of the saved layout, so every change is saved.
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        Save();
        base.OnPropertyChanged(e);
    }

    public async Task LoadLayoutAsync(CancellationToken ct = default)
    {
        if (store is null)
            return;
        var expected = revision;
        try
        {
            var preferences = (await store.LoadLayoutAsync(ct)).Normalize();
            if (expected != revision)
                return;
            Apply(preferences);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { statusBar.Notice = LayoutText.NotLoaded; }
    }
    private void Apply(SavedLayout preferences)
    {
        applying = true;
        try
        {
            DiffMode = preferences.Mode;
            DiffSplitRatio = preferences.SplitRatio;
            DiffFilesRatio = preferences.FilesRatio;
            ThemeIndex = preferences.Theme;
            HistoryPanelRatio = preferences.HistoryRatio;
            StagePanelRatio = preferences.StageRatio;
            CommitPanelRatio = preferences.CommitRatio;
            AutoFetchMinutes = preferences.AutoFetch;
            CommitMessageHeight = preferences.MessageHeight;
        }
        finally { applying = false; }
    }

    private void Save()
    {
        if (applying || store is null)
            return;
        var preferences = new SavedLayout(DiffMode, DiffSplitRatio, DiffFilesRatio, ThemeIndex, HistoryPanelRatio, StagePanelRatio, CommitPanelRatio,
            AutoFetchMinutes, CommitMessageHeight).Normalize();
        save = SaveAsync(save, ++revision, preferences);
    }

    private async Task SaveAsync(Task previous, long current, SavedLayout preferences)
    {
        await previous;
        if (current != revision)
            return;
        await Task.Delay(200);
        if (current != revision)
            return;
        try
        {
            await store!.SaveLayoutAsync(preferences, CancellationToken.None);
        }
        catch (Exception) { statusBar.Notice = LayoutText.NotSaved; }
    }
}
