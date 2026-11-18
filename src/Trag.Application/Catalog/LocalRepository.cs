using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.Catalog;

namespace Trag.Application.Catalog;

// One entry of the repository list with the state of its link: unknown until a check answers, and whether one runs now.
// The list keeps the item across rebuilds, so a checked link and a selection survive a reorder.
public sealed partial class LocalRepository(RepositoryEntry entry) : ObservableObject
{
    [ObservableProperty] public partial RepositoryEntry Entry { get; internal set; } = entry;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid), nameof(IsBroken))]
    public partial RepositoryLinkStatus Status { get; internal set; } = RepositoryLinkStatus.Unknown;
    [ObservableProperty] public partial bool IsChecking { get; internal set; }
    public string Name => Entry.Name;
    public string Path => Entry.Path;
    public bool IsValid => Status == RepositoryLinkStatus.Valid;
    public bool IsBroken => Status is RepositoryLinkStatus.Missing or RepositoryLinkStatus.NotRepository or RepositoryLinkStatus.Untrusted
        or RepositoryLinkStatus.NeedsNewerGit or RepositoryLinkStatus.Bare or RepositoryLinkStatus.GitDirectory;

    // The link check's answer; true when the entry itself changed and is worth writing back.
    internal bool Apply(RepositoryProbe probe)
    {
        Status = probe.Status;
        if (probe.Status != RepositoryLinkStatus.Valid)
            return false;
        if (Entry.Branch == probe.Branch && Entry.RemoteUrl == probe.RemoteUrl)
            return false;
        Entry = Entry with
        {
            Branch = probe.Branch,
            RemoteUrl = probe.RemoteUrl
        };
        return true;
    }
}
