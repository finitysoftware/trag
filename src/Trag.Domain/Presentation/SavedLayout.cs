using Trag.Domain.Sync;

namespace Trag.Domain.Presentation;

// The layout as it is saved: the diff mode, the five splitter ratios, the theme, the automatic fetch interval and the
// height of a commit's message in the history's commit pane.
// CommitRatio is the share above the commit area; 1 means the area sits at its natural height, 0.5 is as tall as it gets.
// AutoFetch is the automatic fetch interval in minutes, one of AutoFetchInterval's. MessageHeight is in pixels, 0 for a
// collapsed message; a negative one means none chosen yet, and the pane gives the message a third of itself.
public sealed record SavedLayout(int Mode = 0, double SplitRatio = 0.5, double FilesRatio = 0.36, int Theme = 0, double HistoryRatio = 0.45, double StageRatio = 0.5, double CommitRatio = 1,
    int AutoFetch = 1, double MessageHeight = -1)
{
    private static readonly SavedLayout Defaults = new();

    public SavedLayout Normalize() => new(Enum.IsDefined((DiffLayout)Mode) ? Mode : Defaults.Mode,
        Ratio(SplitRatio, Defaults.SplitRatio),
        Ratio(FilesRatio, Defaults.FilesRatio),
        Enum.IsDefined((ThemeChoice)Theme) ? Theme : Defaults.Theme,
        Ratio(HistoryRatio, Defaults.HistoryRatio),
        Ratio(StageRatio, Defaults.StageRatio),
        CommitRatio > 0 ? Math.Clamp(CommitRatio, 0.5, 1) : Defaults.CommitRatio,
        (int)AutoFetchIntervals.From(AutoFetch),
        double.IsFinite(MessageHeight) && MessageHeight >= 0 ? MessageHeight : Defaults.MessageHeight);

    private static double Ratio(double value, double fallback) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;
}
