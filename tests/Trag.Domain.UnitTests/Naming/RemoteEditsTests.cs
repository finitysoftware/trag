using Trag.Domain.Naming;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Naming;

// What keeps adding, changing or removing a remote back before Git is asked, in the words the Remotes dialog shows.
public sealed class RemoteEditsTests
{
    private static readonly RepositorySnapshot TwoRemotes =
        Snapshots.Create(remotes: [Snapshots.Remote(), Snapshots.Remote("upstream", "https://github.com/acme/trag.git")]);

    [Fact]
    public void WhenARemoteIsAdded_ShouldNeedAFreeValidNameAndAUsableUrl()
    {
        Assert.Equal("", RemoteEdits.AddError(TwoRemotes, "fork", "git@github.com:me/trag.git"));
        Assert.Equal("A remote named origin already exists. Choose another name.", RemoteEdits.AddError(TwoRemotes, "origin", "https://example.com/x.git"));
        Assert.Equal("Enter the remote's URL or path.", RemoteEdits.AddError(TwoRemotes, "fork", " "));
        Assert.Equal("Enter a URL or path without spaces, line breaks or a leading '-'.", RemoteEdits.AddError(TwoRemotes, "fork", "--upload-pack=x"));
    }

    // A password or token in the URL would sit in .git/config in plain text; accounts are kept in Settings instead.
    [Fact]
    public void WhenTheUrlCarriesAPasswordOrToken_ShouldRefuseIt()
    {
        Assert.Equal("Remove the password or token from the URL and add the account in Settings → Integrations instead.",
            RemoteEdits.AddError(TwoRemotes, "fork", "https://me:ghp_secret@github.com/me/trag.git"));
        Assert.Equal("", RemoteEdits.AddError(TwoRemotes, "fork", "https://me@github.com/me/trag.git"));
    }

    // A token works as the user name too, with nothing after it, and would sit in .git/config the same way.
    [Theory]
    [InlineData("https://ghp_secret@github.com/me/trag.git")]
    [InlineData("https://github_pat_secret@github.com/me/trag.git")]
    [InlineData("https://glpat-secret@gitlab.com/me/trag.git")]
    public void WhenTheUrlCarriesATokenAsItsUserName_ShouldRefuseIt(string url) =>
        Assert.Equal("Remove the password or token from the URL and add the account in Settings → Integrations instead.", RemoteEdits.AddError(TwoRemotes, "fork", url));

    // Keeping the name or the URL is no change to refuse; taking another remote's name is.
    [Fact]
    public void WhenARemoteIsChanged_ShouldAllowItsOwnNameAndRefuseAnotherRemotesName()
    {
        Assert.Equal("", RemoteEdits.EditError(TwoRemotes, "upstream", "upstream", null));
        Assert.Equal("", RemoteEdits.EditError(TwoRemotes, "upstream", "acme", "https://github.com/acme/trag2.git"));
        Assert.Equal("A remote named origin already exists. Choose another name.", RemoteEdits.EditError(TwoRemotes, "upstream", "origin", null));
        Assert.Equal("That remote is no longer in this repository. Refresh and try again.", RemoteEdits.EditError(TwoRemotes, "gone", "gone", null));
    }

    // Git up to 2.50, or the config, can leave origin beside origin/b; a name that does not change is no new overlap.
    [Fact]
    public void WhenOnlyTheUrlOfARemoteThatOverlapsAnotherChanges_ShouldNotRefuseItsName() =>
        Assert.Equal("", RemoteEdits.EditError(Snapshots.Create(remotes: [Snapshots.Remote(), Snapshots.Remote("origin/b", "https://github.com/acme/trag.git")]),
            "origin", "origin", "https://example.com/new.git"));

    [Fact]
    public void WhenARemoteIsRemoved_ShouldNeedItToBeThere()
    {
        Assert.Equal("", RemoteEdits.RemoveError(TwoRemotes, "upstream"));
        Assert.Equal("That remote is no longer in this repository. Refresh and try again.", RemoteEdits.RemoveError(TwoRemotes, "gone"));
    }
}
