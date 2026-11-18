using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Sync;

namespace Trag.ViewModels.Sync;

// The repository's sync target as the sidebar's two pickers show it: the remotes and the chosen one, and the upstream
// choices as rows with the chosen one selected, under the picker's search; what Git says is kept separately in
// SyncOverview. Each snapshot's target is a selection of its own, which keeps the previous rows while the target kept its
// choices, and the search always. Changed is the user's change alone: what the target applies stays quiet.
public sealed partial class SyncSelection : ObservableObject
{
    private SyncTargetSelection target;
    private SyncBranchChoice upstream;
    private bool applying;
    public IReadOnlyList<string> RemoteNames => target.RemoteNames;
    public RepositoryRemote? Remote => target.Remote;
    public event Action? Changed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Remote), nameof(RemoteRows), nameof(RemoteLabel))]
    public partial string? RemoteName { get; set; }
    public string RemoteLabel => RemoteName ?? SyncText.ChooseRemote;

    public IReadOnlyList<SyncRemoteRow> RemoteRows => [.. RemoteNames.Select(name => new SyncRemoteRow(name, name == RemoteName))];

    [ObservableProperty]
    public partial IReadOnlyList<SyncBranchRow> Branches { get; private set; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(BranchLabel))]
    public partial SyncBranchRow? SelectedBranch { get; set; }
    public string BranchLabel => SelectedBranch?.Label ?? SyncText.ChooseBranch;

    [ObservableProperty]
    public partial string Search { get; set; }

    // The rows under the search.
    [ObservableProperty]
    public partial IReadOnlyList<SyncBranchRow> Visible { get; private set; } = [];

    private SyncSelection(SyncTargetSelection target, SyncBranchChoice upstream, IReadOnlyList<SyncBranchRow> branches, string search)
    {
        this.target = target;
        this.upstream = upstream;
        Search = search;
        RemoteName = target.RemoteName;
        Apply(branches, upstream.Chosen);
    }

    public static SyncSelection Empty() => new(SyncTargetSelection.Empty, SyncBranchChoice.None, [], "");

    // Another snapshot's target is another selection.
    public SyncSelection Following(SyncTargetSelection next, SyncBranchChoice choice) => new(next, choice, RowsFor(choice), Search);

    // The same target's new choice, the user's own or the branches a read offered, shown in place.
    public void Show(SyncTargetSelection next, SyncBranchChoice choice)
    {
        var rows = RowsFor(choice);
        (target, upstream) = (next, choice);
        Apply(rows, choice.Chosen);
    }

    // A row stays the same row for as long as the target keeps its choices.
    private IReadOnlyList<SyncBranchRow> RowsFor(SyncBranchChoice choice) =>
        ReferenceEquals(choice.Branches, upstream.Branches) ? Branches : choice.Branches.Select(branch => new SyncBranchRow(branch)).ToArray();

    private void Apply(IReadOnlyList<SyncBranchRow> branches, SyncBranch? chosen)
    {
        applying = true;
        Branches = branches;
        Visible = Filter(Search);
        SelectedBranch = branches.FirstOrDefault(row => row.Branch == chosen);
        applying = false;
    }

    partial void OnRemoteNameChanged(string? value)
    {
        target = target with { RemoteName = value };
        Apply([], null);
        Changed?.Invoke();
    }

    partial void OnSearchChanged(string value) => Visible = Filter(value);

    private IReadOnlyList<SyncBranchRow> Filter(string text) => [.. Branches.Where(row => SyncText.Matches(row.Branch, text))];

    partial void OnSelectedBranchChanged(SyncBranchRow? oldValue, SyncBranchRow? newValue)
    {
        oldValue?.IsChosen = false;
        newValue?.IsChosen = true;
        if (!applying) Changed?.Invoke();
    }
}
