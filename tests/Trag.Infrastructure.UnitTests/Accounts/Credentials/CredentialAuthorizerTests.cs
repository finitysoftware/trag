using System.Text;
using Trag.Infrastructure.Accounts.Credentials;

namespace Trag.Infrastructure.UnitTests.Accounts.Credentials;

// The rule alone, without the pipe: which prompt from which operation about which destination may be answered.
public sealed class CredentialAuthorizerTests
{
    private const string Operation = "operation-1";
    private static readonly Uri Origin = new("https://gitlab.example:8443/team/project.git");
    private static readonly byte[] Secret = "only-for-this-repo"u8.ToArray();

    private static CredentialAuthorizer Authorizer(CancellationToken lifetime = default, Uri? origin = null) =>
        new(origin ?? Origin, "alice", Secret, Operation, lifetime);

    private static CredentialRequest Password(string url) => new(Operation, "Password for '" + url + "': ");
    private static CredentialRequest Username(string url) => new(Operation, "Username for '" + url + "': ");

    [Fact]
    public void WhenGitAsksForTheUsername_ShouldAnswerWithTheName()
    {
        var authorizer = Authorizer();
        Assert.Equal("alice"u8.ToArray(), authorizer.Authorize(Username("https://gitlab.example:8443/team/project.git")));
    }

    // Git asks for the user name first, then for the password.
    [Fact]
    public void WhenGitAsksForThePassword_ShouldAnswerWithTheSecret()
    {
        var authorizer = Authorizer();
        Assert.Equal("alice"u8.ToArray(), authorizer.Authorize(Username("https://gitlab.example:8443/team/project.git")));
        Assert.Equal(Secret, authorizer.Authorize(Password("https://alice@gitlab.example:8443/team/project.git")));
    }

    [Fact]
    public void WhenTheAnswerIsCleared_ShouldKeepTheSecret()
    {
        var answer = Authorizer().Authorize(Password("https://alice@gitlab.example:8443/team/project.git"))!;
        Array.Clear(answer);
        Assert.Equal("only-for-this-repo", Encoding.UTF8.GetString(Secret));
    }

    [Theory]
    [InlineData("What is your password for 'https://alice@gitlab.example:8443/team/project.git': ")]
    [InlineData("Password for 'https://alice@gitlab.example:8443/team/project.git'")]
    [InlineData("Passphrase for key '/home/alice/.ssh/id_ed25519': ")]
    public void WhenThePromptIsNotOneOfTheTwoGitAsks_ShouldRefuseIt(string prompt) => Assert.Null(Authorizer().Authorize(new(Operation, prompt)));

    [Theory]
    [InlineData("https://alice@gitlab.example:8443/team/other.git")]
    [InlineData("https://alice@gitlab.example/team/project.git")]
    [InlineData("https://alice@gitlab.example:8444/team/project.git")]
    [InlineData("https://alice@evil.invalid/team/project.git")]
    [InlineData("http://alice@gitlab.example:8443/team/project.git")]
    [InlineData("https://alice@gitlab.example:8443/team/project.git?token=1")]
    [InlineData("https://alice@gitlab.example:8443/team/project.git#x")]
    [InlineData("not-a-url")]
    public void WhenTheDestinationIsNotExactlyThisOne_ShouldRefuseIt(string url) => Assert.Null(Authorizer().Authorize(Password(url)));

    [Fact]
    public void WhenThePasswordPromptIsForAnotherUser_ShouldRefuseIt() =>
        Assert.Null(Authorizer().Authorize(Password("https://bob@gitlab.example:8443/team/project.git")));

    [Fact]
    public void WhenTheUsernamePromptAlreadyCarriesAUser_ShouldRefuseIt() =>
        Assert.Null(Authorizer().Authorize(Username("https://alice@gitlab.example:8443/team/project.git")));

    [Fact]
    public void WhenTheRequestComesFromAnotherOperation_ShouldRefuseIt() =>
        Assert.Null(Authorizer().Authorize(new("operation-2", "Password for 'https://alice@gitlab.example:8443/team/project.git': ")));

    [Fact]
    public void WhenTheOperationIsOver_ShouldAnswerNothing()
    {
        using var over = new CancellationTokenSource();
        over.Cancel();
        Assert.Null(Authorizer(over.Token).Authorize(Password("https://alice@gitlab.example:8443/team/project.git")));
    }

    [Fact]
    public void WhenATransferAsksFourTimes_ShouldAnswerEachTime()
    {
        var authorizer = Authorizer();
        for (var i = 0; i < 4; i++) Assert.NotNull(authorizer.Authorize(Password("https://alice@gitlab.example:8443/team/project.git")));
    }

    // Four answers are enough for any one transfer.
    [Fact]
    public void WhenATransferWasAnsweredFourTimes_ShouldAnswerNeitherPromptAgain()
    {
        var authorizer = Authorizer();
        for (var i = 0; i < 4; i++) Assert.NotNull(authorizer.Authorize(Password("https://alice@gitlab.example:8443/team/project.git")));
        Assert.Null(authorizer.Authorize(Password("https://alice@gitlab.example:8443/team/project.git")));
        Assert.Null(authorizer.Authorize(Username("https://gitlab.example:8443/team/project.git")));
    }

    [Fact]
    public void WhenTheOriginIsTheHostAlone_ShouldAnswerForAnyPathOnIt()
    {
        var authorizer = Authorizer(origin: new("https://gitlab.example:8443/"));
        Assert.Equal(Secret, authorizer.Authorize(Password("https://alice@gitlab.example:8443/anything/at/all.git")));
    }
}
