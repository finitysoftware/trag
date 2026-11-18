using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Sync;
using Trag.Domain.Presentation.Text;

namespace Trag.ViewModels.Sync;

// The automatic fetch as the settings card and the Fetch button show it: the interval to choose and the button's tip.
// The model runs the schedule; the application starts, wakes and stops it through the workbench.
public sealed class AutoFetchViewModel : ObservableObject
{
    private readonly AutoFetchModel model;

    public AutoFetchViewModel(AutoFetchModel model)
    {
        this.model = model;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AutoFetchModel.Interval)) OnPropertyChanged(nameof(Selected));
            if (e.PropertyName is nameof(AutoFetchModel.Interval) or nameof(AutoFetchModel.IsStopped)) OnPropertyChanged(nameof(FetchTip));
        };
    }

    public IReadOnlyList<AutoFetchOption> Options => AutoFetchText.Options;
    public AutoFetchOption Selected
    {
        get => AutoFetchText.Option(model.Interval);
        set => model.Interval = value.Interval;
    }
    public string FetchTip => AutoFetchText.FetchTip(model.Interval, model.IsStopped);
}
