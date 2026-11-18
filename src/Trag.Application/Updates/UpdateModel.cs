using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Ports.Updates;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Updates;

namespace Trag.Application.Updates;

// The update check on the About page: the user asks for it, one at a time. It says what it is doing and what it
// learned, and offers the download, in place of another check, only once a newer Trag is out.
public sealed partial class UpdateModel(IUpdateChecker checker, string informationalVersion) : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Status), nameof(HasStatus), nameof(CanCheck), nameof(OffersDownload))]
    public partial UpdateState State { get; private set; }
    public UpdateOffer? Offer { get; private set; }

    public bool HasStatus => State != UpdateState.NotChecked;
    public bool CanCheck => State is not (UpdateState.Checking or UpdateState.Available);
    public bool OffersDownload => State == UpdateState.Available;
    public string Status => State switch
    {
        UpdateState.Checking => AboutText.CheckingForUpdates,
        UpdateState.UpToDate => AboutText.UpToDate,
        UpdateState.Available => AboutText.UpdateAvailable(Offer!.LatestVersion),
        UpdateState.Failed => AboutText.UpdateNotChecked,
        _ => "",
    };

    public async Task CheckAsync()
    {
        if (!CanCheck) return;
        State = UpdateState.Checking;
        Offer = await checker.CheckAsync(VersionNumber.Of(informationalVersion));
        State = Offer is null ? UpdateState.Failed : Offer.IsAvailable ? UpdateState.Available : UpdateState.UpToDate;
    }
}
