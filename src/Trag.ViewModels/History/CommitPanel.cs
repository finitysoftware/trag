using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.History;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;

namespace Trag.ViewModels.History;

// The commit pane's content for one read commit, or the heading that stands in its place: the texts over the commit, its
// files as rows and the file chosen among them.
public sealed partial class CommitPanel : ObservableObject
{
    public CommitDetails? Details { get; }
    public string Heading { get; }
    public string Identity { get; } = "";
    public string Signature { get; } = "";
    public string CoAuthorText { get; } = "";
    public string Body { get; } = "";
    public string Comparison { get; } = "";
    public string FileNotice { get; } = "";
    public IReadOnlyList<CommitFileRow> Files { get; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial CommitFileRow? SelectedFile { get; set; }
    public bool HasSelection => SelectedFile is not null;
    public event Action<CommitFileRow?>? SelectionChanged;
    partial void OnSelectedFileChanged(CommitFileRow? value) => SelectionChanged?.Invoke(value);

    private CommitPanel(string heading) => Heading = heading;

    private CommitPanel(CommitDetails details)
    {
        Details = details;
        Heading = details.Commit.Id;
        Identity = CommitText.Identity(details.Commit);
        Signature = CommitText.Signature(details.Signature);
        CoAuthorText = CommitText.CoAuthors(CoAuthors.Of(details.Message));
        Body = CommitText.Message(details.Message);
        Comparison = CommitText.Comparison(details);
        FileNotice = CommitText.Files(details);
        Files = details.Files.Select(file => new CommitFileRow(file, details.Commit.Id)).ToArray();
    }

    public static CommitPanel Empty(string heading = CommitText.SelectCommit) => new(heading);
    public static CommitPanel From(CommitDetails details) => new(details);
}
