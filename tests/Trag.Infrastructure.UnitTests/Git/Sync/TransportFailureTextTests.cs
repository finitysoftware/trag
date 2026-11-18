using System.Text;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Sync;

namespace Trag.Infrastructure.UnitTests.Git.Sync;

// Git's own transfer output, turned into the one sentence that tells the user what to do next.
public sealed class TransportFailureTextTests
{
    private static string Explain(string output) => TransportFailureText.Explain(new(1, [], Encoding.UTF8.GetBytes(output)));

    private static GitResult Failure(string error) => new(128, [], Encoding.UTF8.GetBytes(error));

    [Fact]
    public void WhenAPushIsRejectedAsNonFastForward_ShouldAskToFetchFirst()
    {
        const string expected = "Push was rejected because the destination has commits you do not have. Fetch and review before pulling or pushing again.";
        Assert.Equal(expected, Explain("! [rejected] main -> main (non-fast-forward)"));
    }

    [Fact]
    public void WhenGitHintsToFetchFirst_ShouldAskToFetchFirst()
    {
        const string expected = "Push was rejected because the destination has commits you do not have. Fetch and review before pulling or pushing again.";
        Assert.Equal(expected, Explain("hint: Updates were rejected. Integrate the remote changes (fetch first)."));
    }

    // A remote set to fetch all its tags, whose tag differs from the one of that name here, whether the remote moved
    // it or the tag here was made on another commit.
    [Fact]
    public void WhenAFetchOfAllTagsWouldOverwriteATagHere_ShouldNameTheTag() =>
        Assert.Equal("The remote's tag v1 differs from the tag of that name here, and this remote's tagOpt setting --tags makes every fetch take all of its tags, so nothing was fetched. Delete the tag here to take the remote's, or remove the tagOpt setting.",
            Explain("From ../remote\n ! [rejected]        v1         -> v1  (would clobber existing tag)"));

    [Fact]
    public void WhenAFetchOfAllTagsWouldOverwriteSeveralTagsHere_ShouldNameThemAll() =>
        Assert.Equal("The remote's tags v1, release/2.0 differ from the tags of those names here, and this remote's tagOpt setting --tags makes every fetch take all of its tags, so nothing was fetched. Delete those tags here to take the remote's, or remove the tagOpt setting.",
            Explain("From ../remote\n ! [rejected]        v1          -> v1           (would clobber existing tag)\n ! [rejected]        release/2.0 -> release/2.0  (would clobber existing tag)"));

    [Fact]
    public void WhenTheRemoteRefusesToDeleteItsCurrentBranch_ShouldPointToTheHostingSite()
    {
        const string expected = "The remote refused because this is its default or protected branch. Change that on the hosting site first.";
        Assert.Equal(expected, Explain("remote: error: refusing to delete the current branch: refs/heads/main"));
    }

    [Fact]
    public void WhenTheRemoteRefusesAProtectedBranchUpdate_ShouldPointToTheHostingSite()
    {
        const string expected = "The remote refused because this is its default or protected branch. Change that on the hosting site first.";
        Assert.Equal(expected, Explain("remote: error: GH006: Protected branch update failed for refs/heads/main."));
    }

    [Fact]
    public void WhenGitHubRefusesAWorkflowChangeWithoutTheWorkflowPermission_ShouldAskToAddTheAccountAgain() =>
        Assert.Equal("GitHub refused the push because it changes GitHub Actions workflows and this sign-in lacks the workflow permission. Remove and add the GitHub account again in Settings, then push again.",
            Explain("! [remote rejected] main -> main (refusing to allow an OAuth App to create or update workflow without `workflow` scope)"));

    [Fact]
    public void WhenTheOrganizationRestrictsOAuthApps_ShouldAskToApproveTragThere() =>
        Assert.Equal("GitHub refused the push because the organization restricts OAuth apps. Approve Trag under the organization's third-party access settings, then push again.",
            Explain("remote: error: OAuth App access restrictions prevent this token from accessing the repository."));

    [Fact]
    public void WhenHttpsAuthenticationFails_ShouldPointAtTheAccountAndTheExistingCredentials()
    {
        const string expected = "Git authentication failed. Check the selected account in Settings, its repository permissions, or your existing SSH agent and Git credentials, then retry.";
        Assert.Equal(expected, Explain("fatal: Authentication failed for 'https://example.invalid/repo.git/'"));
    }

    [Fact]
    public void WhenSshRefusesTheKey_ShouldPointAtTheAccountAndTheExistingCredentials()
    {
        const string expected = "Git authentication failed. Check the selected account in Settings, its repository permissions, or your existing SSH agent and Git credentials, then retry.";
        Assert.Equal(expected, Explain("git@example.invalid: Permission denied (publickey)."));
    }

    // Trag never lets Git ask for a credential during a transfer, so a remote with nothing saved for it fails before the
    // network is reached. That is a sign-in problem with its own remedy, not a connection problem.
    [Fact]
    public void WhenGitCannotAskForAUsername_ShouldNameTheRemedyInsteadOfTheNetworkAndCountAsRefusedCredentials()
    {
        const string expected = "Git has no saved credentials for this remote, and Trag never asks for them during a transfer. Choose a Trag account for this remote in the sync panel, or sign in once with Git's own credential helper from a terminal, then retry.";
        const string username = "fatal: Cannot prompt because user interactivity has been disabled.\nfatal: could not read Username for 'https://github.com': terminal prompts disabled";
        Assert.Equal(expected, Explain(username));
        Assert.True(TransportFailureText.RefusesCredentials(new(128, [], Encoding.UTF8.GetBytes(username))));
    }

    [Fact]
    public void WhenGitCannotAskForAPassword_ShouldNameTheRemedyInsteadOfTheNetwork()
    {
        const string expected = "Git has no saved credentials for this remote, and Trag never asks for them during a transfer. Choose a Trag account for this remote in the sync panel, or sign in once with Git's own credential helper from a terminal, then retry.";
        Assert.Equal(expected, Explain("fatal: could not read Password for 'https://alice@example.invalid': terminal prompts disabled"));
    }

    // The account's token was sent and refused, and Git's next ask for a password was refused too: what failed is the
    // sign-in, not a missing one.
    [Fact]
    public void WhenAuthenticationFailedPrecedesCouldNotReadPassword_ShouldSayAuthenticationFailed() =>
        Assert.Equal(TransportText.AuthenticationFailed, Explain("remote: Invalid username or password.\nfatal: Authentication failed for 'https://github.com/acme/trag.git/'\nerror: unable to read askpass response from 'trag-askpass'\nfatal: could not read Password for 'https://alice@github.com': terminal prompts disabled"));

    [Fact]
    public void WhenHttpsAuthenticationFailed_ShouldCountAsRefusedCredentials() =>
        Assert.True(TransportFailureText.RefusesCredentials(Failure("fatal: Authentication failed for 'https://example.invalid/repo.git/'")));

    [Fact]
    public void WhenSshRefusedTheKey_ShouldCountAsRefusedCredentials() =>
        Assert.True(TransportFailureText.RefusesCredentials(Failure("git@example.invalid: Permission denied (publickey).")));

    // Git 2.43 to 2.55 on Windows, fetching into a tracking ref whose folder the user may not write to: the operating
    // system refused a file here, the remote refused nothing, so no account is to blame and auto-fetch does not stop.
    [Fact]
    public void WhenALocalPermissionDeniedComesFromARefLock_ShouldNotSaySignInFailed()
    {
        var failure = Failure("From ../remote\n * [new branch]      main       -> origin/main\n"
            + "error: cannot lock ref 'refs/remotes/origin/main': Unable to create 'C:/work/.git/refs/remotes/origin/main.lock': Permission denied");
        Assert.Equal((TransportText.Failed, false), (TransportFailureText.Explain(failure), TransportFailureText.RefusesCredentials(failure)));
    }

    // Only a refused sign-in is the credentials; anything else is the network.
    [Fact]
    public void WhenTheHostCannotBeResolved_ShouldNotCountAsRefusedCredentials() =>
        Assert.False(TransportFailureText.RefusesCredentials(Failure("fatal: unable to access 'https://example.invalid/repo.git/': Could not resolve host")));

    // GitHub and GitLab answer an address they have no repository at, or one the credentials may not see, with HTTP 404,
    // which Git reports in its own words below the host's.
    [Fact]
    public void WhenTheHostHasNoRepositoryAtTheAddress_ShouldSaySoAndCountAsAMissingRepository()
    {
        const string output = "remote: Repository not found.\nfatal: repository 'https://github.com/nadia/sample-app/' not found";
        Assert.Equal("The host has no repository at this remote's address, or the account in use cannot see it. Check the address and the account, then retry.",
            Explain(output));
        Assert.True(TransportFailureText.MissesRepository(output));
    }

    // A branch's Git LFS files go before its commits, and Git LFS answers the same 404 in its own words.
    [Fact]
    public void WhenGitLfsFindsNoRepositoryAtTheAddress_ShouldCountAsAMissingRepository() =>
        Assert.True(TransportFailureText.MissesRepository("Uploading LFS objects:   0% (0/1), 0 B | 0 B/s, done.\nbatch response: Repository or object not found: https://github.com/nadia/sample-app.git/info/lfs/objects/batch\nCheck that it exists and that you have proper access to it"));

    [Fact]
    public void WhenTheHostCannotBeResolved_ShouldNotCountAsAMissingRepository() =>
        Assert.False(TransportFailureText.MissesRepository("fatal: unable to access 'https://example.invalid/repo.git/': Could not resolve host"));

    [Fact]
    public void WhenGitSaysAnythingElse_ShouldAskToCheckTheRemoteAndReviewTheDetailsBelow() =>
        Assert.Equal("Git rejected the transfer or the connection failed. Check the remote, network, account permissions and organization approval; details are shown below.",
            Explain("fatal: unable to access 'https://example.invalid/repo.git/': Could not resolve host"));

    [Fact]
    public void WhenTheOutputMatchesTwoExplanations_ShouldGiveTheMostSpecificOne() =>
        // A rejected non-fast-forward and a failed sign-in; the non-fast-forward rule is checked first and answers.
        Assert.StartsWith("Push was rejected because the destination has commits you do not have.",
            Explain("! [rejected] main -> main (non-fast-forward)\nfatal: Authentication failed"));
}
