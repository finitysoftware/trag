using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.Presentation.Text;

namespace Trag.Application.Live;

// Everything the status bar and the banners show: the status line, the error and notice banners, the repository
// warning, and the details of the last write or sync operation.
public sealed partial class StatusBarState : ObservableObject
{
    [ObservableProperty] public partial string Status { get; set; } = SessionText.Ready;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))] public partial string ErrorMessage { get; set; } = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNotice))] public partial string Notice { get; set; } = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasWarning))] public partial string RepositoryWarning { get; set; } = "";
    [ObservableProperty] public partial string WriteDetails { get; set; } = "";
    [ObservableProperty] public partial string SyncDetails { get; set; } = "";
    // The text and flag of the sync-style operation in progress (fetch, pull, push, merge, publish).
    [ObservableProperty] public partial string SyncOperationText { get; set; } = "";

    public bool HasError => ErrorMessage.Length != 0;
    public bool HasNotice => Notice.Length != 0;
    public bool HasWarning => RepositoryWarning.Length != 0;
    // The status bar's Details shows the git output of the last write or sync operation; banners carry only the message.
    public string OperationDetails => WriteDetails.Length != 0 ? WriteDetails : SyncDetails;
    public bool HasOperationDetails => OperationDetails.Length != 0;

    partial void OnWriteDetailsChanged(string value) => NotifyOperationDetails();
    partial void OnSyncDetailsChanged(string value) => NotifyOperationDetails();
    private void NotifyOperationDetails()
    {
        OnPropertyChanged(nameof(OperationDetails)); OnPropertyChanged(nameof(HasOperationDetails));
    }
}
