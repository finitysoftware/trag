using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.TestSupport;
using Trag.ViewModels.Shell;
using Trag.ViewModels.Status;

namespace Trag.ViewModels.UnitTests.Shell;

// How far one change travels. A write precondition has to reach every feature that writes; a keystroke in the commit
// message and a click in the file list have no business reaching any of them. Without this the shell is free to widen
// a trigger by accident, and nothing fails.
public sealed class NotificationReachTests
{
    private const string EveryWriteCommand =
        "Commit CreateBranch DeleteBranch Detach Discard Fetch PrepareCommit Publish Push RefreshCatalog Stage Stash Unstage";

    // Records which write commands re-evaluated, by name, so a trigger's reach is a set and not a number, and counts
    // the property notifications the two widest panels receive on the way.
    private sealed class Reach
    {
        private readonly SortedSet<string> touched = [];
        public int PublishNotifications { get; private set; }
        public int SyncNotifications { get; private set; }

        public Reach(ShellViewModel model)
        {
            Watch("Commit", model.Commit.CommitCommand); Watch("Stage", model.Commit.StageSelectedCommand);
            Watch("Unstage", model.Commit.UnstageSelectedCommand); Watch("Discard", model.Discard.DiscardSelectedCommand);
            Watch("Fetch", model.Sync.FetchCommand); Watch("Push", model.Sync.PushCommand);
            Watch("CreateBranch", model.Branches.OpenCreateBranchCommand); Watch("DeleteBranch", model.Branches.Delete.ConfirmCommand);
            Watch("Detach", model.GoTo.DetachAtCommitCommand); Watch("Stash", model.Stash.OpenStashCommand);
            Watch("Publish", model.Publish.OpenPublishCommand); Watch("RefreshCatalog", model.Catalog.RefreshCatalogCommand);
            Watch("PrepareCommit", model.PrepareCommitCommand);
            ((INotifyPropertyChanged)model.Publish).PropertyChanged += (_, _) => PublishNotifications++;
            ((INotifyPropertyChanged)model.Sync).PropertyChanged += (_, _) => SyncNotifications++;
        }

        private void Watch(string name, IRelayCommand command) => command.CanExecuteChanged += (_, _) => touched.Add(name);
        public string Take() { var names = string.Join(" ", touched); touched.Clear(); return names; }
    }

    private static async Task<ShellViewModel> Opened()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Staged("a.txt"), Snapshots.Modified("b.txt")]);
        var model = h.Shell(withDiff: false);
        await model.Session.OpenRepositoryAsync("fixture");
        return model;
    }

    // CanCommit() is the only reader of the message.
    [Fact]
    public async Task WhenACommitMessageIsTyped_ShouldReachOnlyTheCommitCommandAndNoPanel()
    {
        await using var model = await Opened();
        var reach = new Reach(model);

        model.Commit.CommitMessage = "Subject";

        Assert.Equal("Commit", reach.Take());
        Assert.Equal(0, reach.PublishNotifications);
        Assert.Equal(0, reach.SyncNotifications);
    }

    // Stage and Discard read the unstaged selection, Unstage the staged one. Commit comes along because the commit
    // pane refreshes its three commands and its two button captions together, which is one notification too many
    // and not worth a narrower method.
    [Fact]
    public async Task WhenAFileIsSelected_ShouldReachOnlyTheCommitPaneAndDiscard()
    {
        await using var model = await Opened();
        var reach = new Reach(model);

        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];

        Assert.Equal("Commit Discard Stage Unstage", reach.Take());
        Assert.Equal(0, reach.PublishNotifications);
        Assert.Equal(0, reach.SyncNotifications);
    }

    // This one has to be wide: the unreviewed-outcome flag is a precondition of every write there is.
    [Fact]
    public async Task WhenAWritePreconditionChanges_ShouldReachEveryWriteCommand()
    {
        await using var model = await Opened();
        var reach = new Reach(model);

        model.Repository.NeedsReview = true;

        Assert.Equal(EveryWriteCommand, reach.Take());
    }
}
