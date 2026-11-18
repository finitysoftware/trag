using Trag.Infrastructure.Git.Catalog;

namespace Trag.Infrastructure.UnitTests.Git.Catalog;

// Git's own clone output, turned into the one sentence the picker shows above the details.
public sealed class CloneFailureTextTests
{
    [Theory]
    [InlineData("fatal: Authentication failed for 'https://example.invalid/repo.git/'")]
    [InlineData("git@example.invalid: Permission denied (publickey).")]
    [InlineData("fatal: could not read Username for 'https://example.invalid': terminal prompts disabled")]
    [InlineData("remote: HTTP 403 Forbidden")]
    public void WhenTheSignInIsRefused_ShouldPointAtTheAccountAndTheExistingCredentials(string details) =>
        Assert.Equal("Git authentication failed. Check the account in Settings and its repository permissions, or your existing Git credentials and SSH agent.",
            CloneFailureText.Explain(details));

    [Theory]
    [InlineData("fatal: unable to access 'https://example.invalid/repo.git/': Could not resolve host: example.invalid")]
    [InlineData("fatal: Could not resolve host: example.invalid")]
    public void WhenTheAddressCannotBeReached_ShouldAskForTheNetworkAndTheAddress(string details) =>
        Assert.Equal("The repository address could not be reached. Check your network and the address, then retry.",
            CloneFailureText.Explain(details));

    [Fact]
    public void WhenTheTargetIsOccupied_ShouldAskForAnotherFolder() =>
        Assert.Equal("The target folder already exists and is not empty. Choose another folder.",
            CloneFailureText.Explain("fatal: destination path 'repo' already exists and is not an empty directory."));

    // GitHub and GitLab answer 404 for a repository that is not there and for one the account cannot see, as they do to
    // a fetch or a push.
    [Fact]
    public void WhenTheHostHasNoRepositoryAtTheAddress_ShouldPointAtTheAddressAndTheAccount() =>
        Assert.Equal("The host has no repository at this address, or the account in use cannot see it. Check the address and the account, then retry.",
            CloneFailureText.Explain("Cloning into 'none'...\nremote: Repository not found.\nfatal: repository 'https://github.com/acme/none.git/' not found"));

    [Fact]
    public void WhenNoRuleMatches_ShouldSendTheUserToTheDetails() =>
        Assert.Equal("Git could not clone this repository. Review the details below, then retry.",
            CloneFailureText.Explain("fatal: 'C:/nowhere' does not appear to be a git repository"));

    [Fact]
    public void WhenTwoRulesMatch_ShouldAnswerWithTheFirst() =>
        // Git says both; the sign-in rule is checked first, so that is the sentence.
        Assert.StartsWith("Git authentication failed.",
            CloneFailureText.Explain("fatal: Authentication failed\nfatal: unable to access 'https://example.invalid/repo.git/'"));

    [Fact]
    public void WhenTheOutputIsInAnotherCase_ShouldStillMatchTheRule() =>
        Assert.Equal("The target folder already exists and is not empty. Choose another folder.",
            CloneFailureText.Explain("ALREADY EXISTS"));
}
