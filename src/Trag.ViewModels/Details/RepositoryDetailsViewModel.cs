using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Details;
using Trag.Application.Live;
using Trag.Domain.Details;
using Trag.Domain.Presentation.Text;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.Details;

// The Repository details dialog: who wrote the current branch and what the repository holds, read again each time it
// opens and dropped when it closes. The repository's folder is shown in the file manager or copied through the window,
// and a person chosen is found in the search by their address.
public sealed partial class RepositoryDetailsViewModel : ObservableObject
{
    private readonly RepositoryDetailsModel details;
    private readonly Repository repository;

    public RepositoryDetailsViewModel(RepositoryDetailsModel details)
    {
        this.details = details;
        repository = details.Repository;
        Details.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Details.Value)) OnPropertyChanged(nameof(Overview)); };
    }

    public Loader<RepositoryDetails> Details => details.Details;
    [ObservableProperty] public partial bool IsOpen { get; private set; }
    public RepositoryOverview? Overview => details.Overview;
    // The window's shell, which shows folders and copies; the composition links it, and the search a person opens.
    public Func<IShellHost?> Host { get; set; } = () => null;
    public Action<string> SearchHistory { get; set; } = _ => { };

    [RelayCommand]
    private Task OpenAsync()
    {
        IsOpen = true;
        return Details.ReloadAsync();
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        Details.Clear();
    }

    [RelayCommand] private void Reveal() => Host()?.Reveal(repository.Snapshot!.Root);
    [RelayCommand] private void CopyPath() => Host()?.CopyText(repository.Snapshot!.Root);

    [RelayCommand]
    private void ShowPerson(ContributorRow person)
    {
        Close();
        SearchHistory(person.Identity.Email);
    }
}
