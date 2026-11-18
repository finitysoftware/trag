using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.Branches;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;
using Trag.Domain.Status;
using Trag.ViewModels.Blocks;

namespace Trag.ViewModels.Publish;

// The publish dialog's input over the repository facts in Context. Opening starts from the context again; only the
// chosen target branch survives when the list still has it. Notice carries the outcome of the last attempt.
public sealed partial class PublishDraft : Form
{
    private BranchCatalog branches;
    public PublishTargetList Targets { get; } = new();

    public PublishDraft(PublishContext context, BranchCatalog branches)
    {
        this.branches = branches;
        Context = context;
        Targets.PropertyChanged += TargetChanged;
        ResetFields();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle), nameof(Summary), nameof(ShowRemoteName))]
    [NotifyPropertyChangedFor(nameof(RemoteError), nameof(HasRemoteError), nameof(IsValid))]
    public partial PublishContext Context { get; private set; } = PublishContext.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProviderMode), nameof(IsUrlMode), nameof(IsBranchMode), nameof(ShowModeSwitch))]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Subtitle), nameof(ShowRemoteName), nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(RemoteError), nameof(HasRemoteError), nameof(IsValid))]
    public partial PublishKind Mode { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(NameError), nameof(HasNameError), nameof(IsValid))]
    public partial string Name { get; set; } = "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsPublic))]
    public partial bool IsPrivate { get; set; } = true;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(RemoteError), nameof(HasRemoteError), nameof(IsValid))]
    public partial string RemoteUrl { get; set; } = "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(RemoteError), nameof(HasRemoteError), nameof(Summary), nameof(IsValid))]
    public partial string RemoteName { get; set; } = PublishInput.DefaultRemote;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid))]
    public partial RepositoryRemote? ExistingRemote { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsValid))]
    public partial bool PushAfter { get; set; }

    public bool IsProviderMode => Mode == PublishKind.CreateRepository;
    public bool IsUrlMode => Mode == PublishKind.AddRemote;
    public bool IsBranchMode => Mode == PublishKind.ExistingRemote;
    public bool ShowModeSwitch => !IsBranchMode;
    public bool ShowRemoteName => IsUrlMode || (IsProviderMode && Context.HasRemotes);
    public string Title => PublishText.Title(Mode);
    public string Subtitle => PublishText.Subtitle(Mode, Context);
    public string NameError => PublishNames.Error(Name);
    public bool HasNameError => NameError.Length != 0;
    public bool IsPublic => !IsPrivate;
    // The fields as Domain reads them.
    public PublishInput Input => new(Mode, Name, RemoteName, RemoteUrl, ExistingRemote?.Name, Targets.Selected?.Name, PushAfter, IsPrivate);
    public string RemoteError => Input.RemoteError(Context.Remotes);
    public bool HasRemoteError => RemoteError.Length != 0;

    public string Summary => PublishText.Summary(Input, Context, Targets.Summary);

    public bool IsValid => Input.IsValid(Context.Snapshot);

    // A fresh draft over the context, shown: fields from the context; the target chosen before stays selected when the
    // list still has it.
    public void Open(PublishContext context, BranchCatalog branches)
    {
        var target = Targets.Selected?.Name;
        this.branches = branches;
        Context = context;
        Reset();
        UpdateTargets(branches);
        Targets.Select(target);
        IsOpen = true;
    }

    public void UpdateContext(PublishContext context)
    {
        var selected = context.KeepRemote(ExistingRemote);
        var target = Targets.Selected?.Name;
        Context = context;
        // Replacing ComboBox items can clear its two-way selection; apply the retained choice afterwards.
        ExistingRemote = selected;
        UpdateTargets(branches);
        Targets.Select(target);
    }

    partial void OnExistingRemoteChanged(RepositoryRemote? value) => UpdateTargets(branches);

    // The new remote's name may follow the repository's (PublishInput).
    partial void OnNameChanged(string oldValue, string newValue) => RemoteName = PublishInput.RemoteNameFollowing(Context.Snapshot, RemoteName, oldValue, newValue);

    public void UpdateTargets(BranchCatalog branches)
    {
        this.branches = branches;
        Targets.Update(branches, ExistingRemote?.Name, Context.Snapshot?.Branch);
    }

    private void TargetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PublishTargetList.Summary))
            OnPropertyChanged(nameof(Summary));
        if (e.PropertyName == nameof(PublishTargetList.Selected))
            OnPropertyChanged(nameof(IsValid));
    }

    protected override void ResetFields()
    {
        var defaults = PublishInput.Defaults(Context.Snapshot, Context.RepositoryName);
        Name = defaults.RepositoryName;
        IsPrivate = defaults.IsPrivate;
        RemoteUrl = defaults.RemoteUrl;
        RemoteName = defaults.RemoteName;
        PushAfter = defaults.Push;
        ExistingRemote = Context.PreferredRemote;
        Mode = defaults.Kind;
    }
}
