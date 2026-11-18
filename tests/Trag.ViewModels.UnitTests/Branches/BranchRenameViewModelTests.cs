using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.Branches;

// A local branch renamed from its menu: the dialog opens with the branch's name to change, refuses a name another branch
// has or the same name, sends the rename, and stays open with Git's reason when it is refused.
public sealed class BranchRenameViewModelTests
{
    private static async Task<(ShellHarness Harness, ShellViewModel Model)> OpenedAsync()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(identity: false);
        h.Branches.Local.AddRange(["topic", "other"]);
        h.Writer.Result = new(GitWriteOutcome.Succeeded, "Done.");
        var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Branches.List.Completion;
        return (h, model);
    }

    [Fact]
    public async Task WhenABranchIsRenamedFromItsMenu_ShouldOpenWithItsNameAndSendTheNewOne()
    {
        var (h, model) = await OpenedAsync();
        await using var _ = model;
        var branches = model.Branches;
        branches.IsPickerOpen = true;

        branches.RequestRenameCommand.Execute(branches.Rows.Local.Single(row => row.Name == "topic"));

        Assert.Equal((true, "topic", false, BranchText.RenameSubtitle("topic")), (branches.Rename.IsOpen, branches.Rename.Name, branches.IsPickerOpen, branches.Rename.Subtitle));
        Assert.False(branches.RenameBranchCommand.CanExecute(null));
        branches.Rename.Name = " feature/topic ";
        await branches.RenameBranchCommand.ExecuteAsync(null);

        var sent = Assert.IsType<BranchRenameRequest>(Assert.Single(h.Writer.Requests));
        Assert.Equal(("topic", "feature/topic"), (sent.Name, sent.NewName));
        Assert.False(branches.Rename.IsOpen);
    }

    // Git moves the branch out of its own way first, so it can be renamed into a folder of its own old name.
    [Fact]
    public async Task WhenTheNewNameNestsInTheBranchsOwnName_ShouldRenameIt()
    {
        var (h, model) = await OpenedAsync();
        await using var _ = model;
        var branches = model.Branches;
        branches.RequestRenameCommand.Execute(branches.Rows.Local.Single(row => row.Name == "topic"));

        branches.Rename.Name = "topic/old";

        Assert.False(branches.Rename.HasNameError);
        await branches.RenameBranchCommand.ExecuteAsync(null);
        Assert.Equal("topic/old", Assert.IsType<BranchRenameRequest>(Assert.Single(h.Writer.Requests)).NewName);
    }

    // The current branch can be renamed too; a name another branch has is refused before Git is asked.
    [Fact]
    public async Task WhenTheNewNameIsTaken_ShouldSaySoAndSendNothing()
    {
        var (h, model) = await OpenedAsync();
        await using var _ = model;
        var branches = model.Branches;
        branches.RequestRenameCommand.Execute(branches.Rows.Current);

        branches.Rename.Name = "other";

        Assert.Equal(("main", true), (branches.Rename.Branch!.Name, branches.Rename.HasNameError));
        Assert.False(branches.RenameBranchCommand.CanExecute(null));
        Assert.Empty(h.Writer.Requests);
    }

    // A branch made elsewhere while the dialog is open, from a terminal or another Trag, takes its name as the list shows it.
    [Fact]
    public async Task WhenTheListReloadsWhileTheDialogIsOpen_ShouldFollowIt()
    {
        var (h, model) = await OpenedAsync();
        await using var _ = model;
        var branches = model.Branches;
        branches.RequestRenameCommand.Execute(branches.Rows.Local.Single(row => row.Name == "topic"));
        branches.Rename.Name = "later";
        var changed = new HashSet<string?>();
        branches.Rename.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        h.Branches.Local.Add("later");
        await branches.List.ReloadAsync();

        Assert.True(branches.Rename.HasNameError);
        Assert.False(branches.RenameBranchCommand.CanExecute(null));
        Assert.Subset(changed, new HashSet<string?> { nameof(branches.Rename.NameError), nameof(branches.Rename.HasNameError), nameof(branches.Rename.IsValid) });
    }

    [Fact]
    public async Task WhenGitRefusesTheRename_ShouldStayOpenWithItsReason()
    {
        var (h, model) = await OpenedAsync();
        await using var _ = model;
        var branches = model.Branches;
        h.Writer.Result = GitWriteResult.Rejected(BranchOutcomeText.Gone);
        branches.RequestRenameCommand.Execute(branches.Rows.Local.Single(row => row.Name == "topic"));
        branches.Rename.Name = "renamed";

        await branches.RenameBranchCommand.ExecuteAsync(null);

        Assert.Equal((true, BranchOutcomeText.Gone), (branches.Rename.IsOpen, branches.Rename.Notice));
        branches.CancelRenameCommand.Execute(null);
        Assert.False(branches.Rename.IsOpen);
    }
}
