using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.Accounts;
using Trag.Domain.Catalog;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Presentation;

namespace Trag.ViewModels.Repositories;

// One hosted repository of an account as the picker shows it, with the local clone the catalog matched to it. The
// catalog's commands take the row as their parameter.
public sealed partial class RemoteRepositoryRow(RemoteRepository repository, AccountInfo account, TimeProvider clock) : ObservableObject
{
    public RemoteRepository Repository { get; } = repository;
    public AccountInfo Account { get; } = account;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsCloned), nameof(SubtitleText), nameof(Note), nameof(StateText), nameof(IconKind))]
    public partial LocalRepositoryRow? Local { get; set; }
    public string FullName => Repository.FullName;
    public string Name => Repository.Name;
    public bool IsCloned => Local is not null;
    public string SubtitleText => CatalogText.RemoteSubtitle(Repository, Local?.Path, clock.GetUtcNow());
    public string Note => CatalogText.RemoteNote(Local?.Path);
    public string StateText => CatalogText.RemoteState(IsCloned);
    public string VisibilityText => CatalogText.Visibility(Repository.IsPrivate);
    public string DefaultBranchText => CatalogText.Branch(Repository.DefaultBranch);
    public string UpdatedText => CatalogText.Updated(Repository, clock.GetUtcNow());
    public string WebText => CatalogText.WebText(Repository.WebUrl);
    public string ProviderName => AccountText.ProviderName(Account.Provider);
    public string ProviderIconKind => IconKinds.Account(Account.Provider);
    public string ViewText => CatalogText.ViewOn(ProviderName);
    public string IconKind => IconKinds.RemoteRepository(IsCloned);
}
