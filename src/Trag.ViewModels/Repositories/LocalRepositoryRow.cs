using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Catalog;
using Trag.Domain.Catalog;
using Trag.Domain.Presentation.Text;

namespace Trag.ViewModels.Repositories;

// One entry of the repository list as the picker shows it: the texts over the list's item, which holds the entry and
// the state of its link. The catalog's commands take the row as their parameter.
public sealed class LocalRepositoryRow : ObservableObject
{
    private readonly TimeProvider clock;

    public LocalRepositoryRow(LocalRepository item, TimeProvider clock)
    {
        Item = item;
        this.clock = clock;
        item.PropertyChanged += (_, e) =>
        {
            string[] names = e.PropertyName switch
            {
                nameof(LocalRepository.Entry) => [nameof(Entry), nameof(Name), nameof(Path), nameof(BranchText), nameof(RemoteText), nameof(LastOpenedText)],
                nameof(LocalRepository.Status) => [nameof(Status), nameof(IsValid), nameof(IsBroken), nameof(StatusText), nameof(LinkText)],
                nameof(LocalRepository.IsChecking) => [nameof(IsChecking), nameof(StatusText), nameof(LinkText)],
                _ => []
            };
            foreach (var name in names) OnPropertyChanged(name);
        };
    }

    public LocalRepository Item { get; }
    public RepositoryEntry Entry => Item.Entry;
    public RepositoryLinkStatus Status => Item.Status;
    public bool IsChecking => Item.IsChecking;
    public string Name => Item.Name;
    public string Path => Item.Path;
    public string BranchText => CatalogText.Branch(Entry.Branch);
    public string RemoteText => CatalogText.Remote(Entry);
    public string LastOpenedText => CatalogText.LastOpened(Entry, clock.GetUtcNow());
    public bool IsValid => Item.IsValid;
    public bool IsBroken => Item.IsBroken;
    public string StatusText => CatalogText.LinkStatus(Status, IsChecking);
    public string LinkText => CatalogText.Link(Status, IsChecking);
}
