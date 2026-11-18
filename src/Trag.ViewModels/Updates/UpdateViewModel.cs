using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Updates;

namespace Trag.ViewModels.Updates;

// Settings -> About over the update model (UpdateModel): the user asks for the check, the page says what it learned
// beside the version, and Download opens the newer release's page in the browser.
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly UpdateModel updates;

    public UpdateViewModel(UpdateModel updates)
    {
        this.updates = updates;
        updates.PropertyChanged += (_, e) => OnPropertyChanged(e);
    }

    public string Status => updates.Status;
    public bool HasStatus => updates.HasStatus;
    public bool CanCheck => updates.CanCheck;
    public bool OffersDownload => updates.OffersDownload;
    // The host's launcher, which opens HTTPS addresses only.
    public Func<Uri, bool>? OpenBrowser { get; set; }

    [RelayCommand] private Task CheckAsync() => updates.CheckAsync();
    [RelayCommand] private void Download() { if (updates.Offer?.Download is { } page) OpenBrowser?.Invoke(page); }
}
