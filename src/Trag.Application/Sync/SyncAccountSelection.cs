using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.Accounts;

namespace Trag.Application.Sync;

public sealed partial class SyncAccountSelection : ObservableObject
{
    private readonly RemoteAccountState? saved;
    public IReadOnlyList<AccountChoice> Items => saved?.Choices ?? [];
    public string Message => saved?.Message ?? "";
    public bool HasChoices => saved?.HasChoices == true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnsaved), nameof(IsConfirmed), nameof(CanSave))]
    public partial AccountChoice? Selected { get; set; }

    public SyncAccountSelection(RemoteAccountState? saved)
    {
        this.saved = saved;
        Selected = saved?.Saved;
    }

    public bool IsUnsaved => saved?.IsUnsaved(Selected?.Id) == true;
    public bool CanSave => saved is not null && Selected is not null;
    public bool IsConfirmed => saved?.IsConfirmed(Selected?.Id) ?? true;
}
