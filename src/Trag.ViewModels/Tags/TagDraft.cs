using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.History;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Tags;
using Trag.ViewModels.Blocks;

namespace Trag.ViewModels.Tags;

// The create-tag dialog: the commit it was opened for, a name judged by Git's rules, a message that makes the tag an
// annotated one, optional unless the repository signs its tags, and, when the repository has a remote, whether the tag
// goes there right after. Whether the name
// is taken is the writer's answer.
public sealed partial class TagDraft : Form
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NameError), nameof(HasNameError), nameof(IsValid))]
    public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string Message { get; set; } = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(MessageLabel), nameof(MessageHint))]
    public partial bool SignsTags { get; private set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Subtitle), nameof(IsValid))]
    public partial HistoryCommit? Commit { get; private set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanPush), nameof(PushLabel))]
    public partial string? Remote { get; private set; }
    [ObservableProperty] public partial bool Push { get; set; }
    public bool CanPush => Remote is not null;
    public string PushLabel => Remote is { } remote ? TagText.PushTo(remote) : "";
    // Where the tag goes after it is created; null for here only.
    public string? PushTo => Push ? Remote : null;
    public string NameError => TagNames.Error(Name);
    public bool HasNameError => NameError.Length != 0;
    public bool IsValid => Commit is not null && TagNames.IsValid(Name) && !TagOperation.LacksMessage(SignsTags, Message);
    public string Subtitle => Commit is { } commit ? TagText.CreateSubtitle(commit.ShortId, commit.Subject) : "";
    public string MessageLabel => TagText.MessageLabel(SignsTags);
    public string MessageHint => TagText.MessageHint(SignsTags);
    public TagOperation ToOperation() => new(Name.Trim(), Commit!.Id, Message);

    public void Open(HistoryCommit commit, string? remote, bool signsTags)
    {
        Reset();
        Commit = commit;
        Remote = remote;
        SignsTags = signsTags;
        Push = remote is not null;
        IsOpen = true;
    }

    protected override void ResetFields()
    {
        Name = "";
        Message = "";
        Commit = null;
        Remote = null;
        Push = false;
    }
}
