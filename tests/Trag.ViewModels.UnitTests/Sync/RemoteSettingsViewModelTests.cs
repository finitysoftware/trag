using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.ViewModels.UnitTests.Sync;

// The Remotes dialog: the repository's remotes listed, one added under a free name at a usable address, one changed
// from the address its configuration keeps, and one removed only after the dialog has said what that means.
public sealed class RemoteSettingsViewModelTests
{
    private const string Address = "https://github.com/acme/trag.git";

    [Fact]
    public async Task WhenTheDialogOpens_ShouldListTheRemotesAndAddOneWithAFreeNameAndAUsableAddress()
    {
        var h = new ShellHarness(); h.Reader.Snapshot = Snapshots.Create(remotes: [Snapshots.Remote()]);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        var remotes = model.RemoteSettings;

        remotes.OpenCommand.Execute(null);

        Assert.True(remotes.IsOpen);
        Assert.Equal(["origin"], remotes.Remotes.Select(remote => remote.Name));
        Assert.Equal("", remotes.AddError); Assert.False(remotes.AddCommand.CanExecute(null));
        remotes.NewName = "origin"; remotes.NewUrl = Address;
        Assert.Equal("A remote named origin already exists. Choose another name.", remotes.AddError); Assert.False(remotes.AddCommand.CanExecute(null));
        remotes.NewName = "upstream";
        Assert.Equal("", remotes.AddError); Assert.True(remotes.AddCommand.CanExecute(null));
        await remotes.AddCommand.ExecuteAsync(null);
        var request = Assert.IsType<RemoteAddRequest>(h.Writer.Requests[0]);
        Assert.Equal(("upstream", Address), (request.Name, request.Url));
        Assert.Equal(("", ""), (remotes.NewName, remotes.NewUrl));
    }

    // The new remote's branches come in right away: it is fetched by its own state, under the name it was added by, unless
    // the box is unticked.
    [Fact]
    public async Task WhenARemoteIsAdded_ShouldFetchItUnlessToldNotTo()
    {
        var h = Adding(fetch: new(GitWriteOutcome.Succeeded, "Fetched upstream."));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        var remotes = model.RemoteSettings;
        remotes.OpenCommand.Execute(null);
        Assert.True(remotes.FetchAfterAdding);
        (remotes.NewName, remotes.NewUrl) = ("upstream ", Address);

        await remotes.AddCommand.ExecuteAsync(null);

        var fetch = Assert.IsType<SyncRequest>(h.Writer.Requests[^1]);
        Assert.Equal((2, SyncAction.Fetch, "upstream"), (h.Writer.Requests.Count, fetch.Action, fetch.Sync.Remote.Name));
        remotes.FetchAfterAdding = false;
        (remotes.NewName, remotes.NewUrl) = ("mirror", Address);
        await remotes.AddCommand.ExecuteAsync(null);
        Assert.IsType<RemoteAddRequest>(h.Writer.Requests[^1]);
    }

    // The remote is added either way; a fetch Git refuses says why.
    [Fact]
    public async Task WhenTheFetchAfterAddingIsRefused_ShouldSayWhyWithTheRemoteAdded()
    {
        var h = Adding(fetch: new(GitWriteOutcome.Rejected, "The remote could not be reached."));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        var remotes = model.RemoteSettings;
        remotes.OpenCommand.Execute(null);
        (remotes.NewName, remotes.NewUrl) = ("upstream", Address);

        await remotes.AddCommand.ExecuteAsync(null);

        Assert.Equal(("The remote could not be reached.", ""), (remotes.Message, remotes.NewName));
        Assert.Equal(["origin", "upstream"], remotes.Remotes.Select(remote => remote.Name));
    }

    // The new remote's state cannot be read for its fetch: the remote stays added, nothing is fetched, and the dialog
    // says the fetch did not start.
    [Fact]
    public async Task WhenTheNewRemoteCannotBeReadForItsFetch_ShouldSayTheFetchDidNotStart()
    {
        var h = Adding(fetch: new(GitWriteOutcome.Succeeded, "Fetched upstream."));
        h.Sync.State = (snapshot, remote, branch) => remote == "upstream" ? throw new TimeoutException("Git did not answer.") : FakeGitSync.DefaultState(snapshot, remote, branch);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        var remotes = model.RemoteSettings;
        remotes.OpenCommand.Execute(null);
        (remotes.NewName, remotes.NewUrl) = ("upstream", Address);

        await remotes.AddCommand.ExecuteAsync(null);

        Assert.Equal(Failures.FetchNotStarted, remotes.Message);
        Assert.Equal(["origin", "upstream"], remotes.Remotes.Select(remote => remote.Name));
        Assert.DoesNotContain(h.Writer.Requests, request => request is SyncRequest);
    }

    // Git does not answer for the configured address: the edit starts from the listed one instead of failing.
    [Fact]
    public async Task WhenTheConfiguredAddressCannotBeRead_ShouldStartTheEditFromTheListedOne()
    {
        var h = new ShellHarness(); h.Reader.Snapshot = Snapshots.Create(remotes: [Snapshots.Remote("origin", "github.com:acme/trag.git")]);
        h.Branches.RemoteUrlFailure = new TimeoutException("Git did not answer.");
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        var remotes = model.RemoteSettings;
        remotes.OpenCommand.Execute(null);

        await remotes.BeginEditCommand.ExecuteAsync(remotes.Remotes[0]);

        Assert.Equal(("origin", "github.com:acme/trag.git"), (remotes.Editing, remotes.EditUrl));
    }

    // origin, and Git adding each remote it is asked to; a fetch ends as given.
    private static ShellHarness Adding(GitWriteResult fetch)
    {
        var h = new ShellHarness(); h.Reader.Snapshot = Snapshots.Create(remotes: [Snapshots.Remote()]);
        h.Writer.Handle = (request, _) =>
        {
            if (request is SyncRequest) return Task.FromResult(fetch);
            if (request is RemoteAddRequest add) h.Reader.Snapshot = Snapshots.Create(remotes: [.. h.Reader.Snapshot.Remotes, Snapshots.Remote(add.Name, add.Url)]);
            return Task.FromResult(new GitWriteResult(GitWriteOutcome.Succeeded, "Done"));
        };
        return h;
    }

    // Git refuses: the dialog says why and keeps what was typed.
    [Fact]
    public async Task WhenGitRefusesTheNewRemote_ShouldSayWhyAndKeepTheForm()
    {
        var h = new ShellHarness(); h.Reader.Snapshot = Snapshots.Create(remotes: [Snapshots.Remote()]);
        h.Writer.Result = new(GitWriteOutcome.Rejected, "Git did not change the remote. See the details below.");
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        var remotes = model.RemoteSettings;
        remotes.OpenCommand.Execute(null);
        remotes.NewName = "upstream"; remotes.NewUrl = Address;

        await remotes.AddCommand.ExecuteAsync(null);

        Assert.Equal("Git did not change the remote. See the details below.", remotes.Message);
        Assert.Equal(("upstream", Address), (remotes.NewName, remotes.NewUrl));
    }

    // The list shows a shortened address; the form starts from the configured one, and an unchanged address is not sent.
    [Fact]
    public async Task WhenARemoteIsEdited_ShouldStartFromItsConfiguredAddressAndSendOnlyWhatChanged()
    {
        var h = new ShellHarness(); h.Reader.Snapshot = Snapshots.Create(remotes: [Snapshots.Remote("origin", "github.com:acme/trag.git")]);
        h.Branches.RemoteUrls["origin"] = "git@github.com:acme/trag.git";
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        var remotes = model.RemoteSettings;
        remotes.OpenCommand.Execute(null);

        await remotes.BeginEditCommand.ExecuteAsync(remotes.Remotes[0]);

        Assert.Equal(("origin", "origin", "git@github.com:acme/trag.git"), (remotes.Editing, remotes.EditName, remotes.EditUrl));
        Assert.False(remotes.SaveCommand.CanExecute(null));
        remotes.EditName = "acme";
        await remotes.SaveCommand.ExecuteAsync(null);
        var rename = Assert.IsType<RemoteEditRequest>(Assert.Single(h.Writer.Requests));
        Assert.Equal(("origin", "acme", (string?)null), (rename.Name, rename.NewName, rename.Url));
        Assert.Null(remotes.Editing);

        await remotes.BeginEditCommand.ExecuteAsync(remotes.Remotes[0]);
        remotes.EditUrl = Address;
        await remotes.SaveCommand.ExecuteAsync(null);
        Assert.Equal(("origin", "origin", (string?)Address), (((RemoteEditRequest)h.Writer.LastRequest!).Name, ((RemoteEditRequest)h.Writer.LastRequest!).NewName, ((RemoteEditRequest)h.Writer.LastRequest!).Url));
    }

    // A password or token kept in the configured address is never shown: the form starts from the address without it, and
    // the address left as it was keeps the configuration's own.
    [Fact]
    public async Task WhenTheConfiguredAddressHoldsCredentials_ShouldNotShowThemInTheEditField()
    {
        var h = new ShellHarness(); h.Reader.Snapshot = Snapshots.Create(remotes: [Snapshots.Remote("origin", "https://github.com/acme/trag.git")]);
        h.Branches.RemoteUrls["origin"] = "https://acme:secret@github.com/acme/trag.git";
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        var remotes = model.RemoteSettings;
        remotes.OpenCommand.Execute(null);

        await remotes.BeginEditCommand.ExecuteAsync(remotes.Remotes[0]);
        remotes.EditName = "upstream";
        await remotes.SaveCommand.ExecuteAsync(null);

        Assert.Equal("https://acme@github.com/acme/trag.git", remotes.EditUrl);
        Assert.Null(Assert.IsType<RemoteEditRequest>(Assert.Single(h.Writer.Requests)).Url);
    }

    // An edit that something keeps back cannot be saved: here the new name is another remote's.
    [Fact]
    public async Task WhenTheEditedNameIsTaken_ShouldSaySoAndNotSave()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(remotes: [Snapshots.Remote("origin", "github.com:acme/trag.git"), Snapshots.Remote("backup", "github.com:acme/backup.git")]);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        var remotes = model.RemoteSettings;
        remotes.OpenCommand.Execute(null);
        await remotes.BeginEditCommand.ExecuteAsync(remotes.Remotes[0]);

        remotes.EditName = "backup";

        Assert.NotEqual("", remotes.EditError); Assert.False(remotes.SaveCommand.CanExecute(null));
    }

    // A configured address Git does not answer with leaves the form the address the list has.
    [Fact]
    public async Task WhenTheConfiguredAddressIsNotRead_ShouldEditTheListedOne()
    {
        var h = new ShellHarness(); h.Reader.Snapshot = Snapshots.Create(remotes: [Snapshots.Remote("origin", "github.com:acme/trag.git")]);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        var remotes = model.RemoteSettings;
        remotes.OpenCommand.Execute(null);

        await remotes.BeginEditCommand.ExecuteAsync(remotes.Remotes[0]);

        Assert.Equal(("origin", "github.com:acme/trag.git"), (remotes.Editing, remotes.EditUrl));
    }

    // The current branch tracks origin, so removing origin leaves it without an upstream; nothing goes before the confirmation.
    [Fact]
    public async Task WhenARemoveIsAsked_ShouldSayWhatItMeansAndRemoveOnlyAfterConfirmation()
    {
        var h = new ShellHarness(); h.Reader.Snapshot = h.Synced();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        var remotes = model.RemoteSettings;
        remotes.OpenCommand.Execute(null);

        remotes.BeginRemoveCommand.Execute(remotes.Remotes[0]);

        Assert.Equal("origin", remotes.Removing);
        Assert.Equal("Removing origin takes its remote-tracking branches out of this repository, and main loses its upstream. Nothing is deleted on the server.",
            remotes.Removal);
        Assert.Empty(h.Writer.Requests);
        await remotes.ConfirmRemoveCommand.ExecuteAsync(null);
        Assert.Equal("origin", Assert.IsType<RemoteRemoveRequest>(Assert.Single(h.Writer.Requests)).Name);
        Assert.Null(remotes.Removing);
    }
}
