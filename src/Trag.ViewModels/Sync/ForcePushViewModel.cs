using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Live;
using Trag.Application.Sync;
using Trag.Domain.Presentation.Text;
using Trag.ViewModels.Blocks;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.Sync;

// The file status banner for a branch rewritten after it was pushed: what Domain says about it, the question asked before
// the remote branch is replaced, and the way to the rewritten history. The force push itself is the repository's remotes'.
public sealed partial class ForcePushViewModel : ObservableObject
{
    private readonly RemotesModel remotes;
    private readonly Repository repository;
    private readonly Navigator navigator;

    public ForcePushViewModel(RemotesModel remotes, Navigator navigator)
    {
        this.remotes = remotes;
        repository = remotes.Repository;
        this.navigator = navigator;
        Replace = new(repository, _ => remotes.ForcePushAsync(), summary => summary.ConfirmText, Failures.PushNotStarted);
        remotes.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(RemotesModel.Overview)) Refresh(); };
        repository.SnapshotApplied += (_, _) => Refresh();
        repository.Requery.Track(RequestForcePushCommand);
    }

    [ObservableProperty] public partial ForcePushSummary Summary { get; private set; } = ForcePushSummary.Empty;
    // The question before the remote branch is replaced, asked from the banner's summary. The banner shows the question of
    // the summary it holds now, so a fetch meanwhile shows in it.
    public Confirmation<ForcePushSummary> Replace { get; }

    // The question belongs to the branch it was asked about; a banner that goes away takes it along.
    private void Refresh()
    {
        Summary = remotes.ForcePush;
        if (!Summary.Shows) Replace.Close();
        repository.Requery.Invalidate();
    }

    private bool CanRequest() => Summary.Shows && remotes.CanForcePush;
    [RelayCommand(CanExecute = nameof(CanRequest))]
    private void RequestForcePush() => Replace.Request(Summary);

    [RelayCommand]
    private void ShowHistory() => navigator.ShowHistory();
}
