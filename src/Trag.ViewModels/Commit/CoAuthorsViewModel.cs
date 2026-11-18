using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Live;
using Trag.Application.Status;
using Trag.Domain.Naming;
using Trag.Domain.Writes;

namespace Trag.ViewModels.Commit;

// The co-author choice over the commit message: the repository's authors, read again each time it opens and narrowed
// by the search, which may also be a typed "Name <email>"; the person chosen goes to whoever writes the message.
public sealed partial class CoAuthorsViewModel : ObservableObject
{
    private readonly CoAuthorsModel people;

    public CoAuthorsViewModel(CoAuthorsModel people)
    {
        this.people = people;
        People.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(People.Value)) Choices = CoAuthorChoices.Find(People.Value, Search); };
    }

    public Loader<IReadOnlyList<GitIdentity>> People => people.People;
    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial string Search { get; set; } = "";
    [ObservableProperty] public partial IReadOnlyList<GitIdentity> Choices { get; private set; } = [];
    public event Action<GitIdentity>? Chosen;

    partial void OnSearchChanged(string value) => Choices = CoAuthorChoices.Find(People.Value, value);

    [RelayCommand]
    private Task OpenAsync()
    {
        Search = "";
        return People.ReloadAsync();
    }

    [RelayCommand]
    private void Choose(GitIdentity person)
    {
        IsOpen = false;
        Chosen?.Invoke(person);
    }
}
