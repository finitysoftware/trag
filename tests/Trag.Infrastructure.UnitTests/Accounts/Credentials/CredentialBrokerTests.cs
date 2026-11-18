using System.IO.Pipes;
using System.Text;
using Trag.Infrastructure.Accounts.Credentials;

namespace Trag.Infrastructure.UnitTests.Accounts.Credentials;

// The broker is the only thing standing between a Git askpass prompt and a stored token; every rule here is a security boundary.
public sealed class CredentialBrokerTests
{
    private static CredentialBroker Broker(string origin = "https://example.invalid/", string user = "alice") => new(new(origin), user, "synthetic-value"u8, CancellationToken.None);

    [Theory]
    [InlineData("http://example.invalid/")]
    [InlineData("https://alice@example.invalid/")]
    [InlineData("https://example.invalid/?token=1")]
    [InlineData("https://example.invalid/#fragment")]
    public void WhenTheOriginIsNotCleanHttps_ShouldRefuseToCreateTheBroker(string origin) =>
        Assert.Throws<ArgumentException>(() => new CredentialBroker(new(origin), "alice", "secret"u8, CancellationToken.None));

    [Fact]
    public void WhenTheUserNameIsEmpty_ShouldRefuseToCreateTheBroker() =>
        Assert.Throws<ArgumentException>(() => new CredentialBroker(new("https://example.invalid/"), "", "secret"u8, CancellationToken.None));

    [Fact]
    public void WhenTheUserNameHasALineBreak_ShouldRefuseToCreateTheBroker() =>
        Assert.Throws<ArgumentException>(() => new CredentialBroker(new("https://example.invalid/"), "a\nb", "secret"u8, CancellationToken.None));

    [Fact]
    public void WhenTheSecretIsEmpty_ShouldRefuseToCreateTheBroker() =>
        Assert.Throws<ArgumentException>(() => new CredentialBroker(new("https://example.invalid/"), "alice", ""u8, CancellationToken.None));

    [Fact]
    public void WhenTheSecretIsOverFourKilobytes_ShouldRefuseToCreateTheBroker() =>
        Assert.Throws<ArgumentException>(() => new CredentialBroker(new("https://example.invalid/"), "alice", new byte[4097], CancellationToken.None));

    [Fact]
    public void WhenTheSecretHasALineBreak_ShouldRefuseToCreateTheBroker()
    {
        Assert.Throws<ArgumentException>(() => new CredentialBroker(new("https://example.invalid/"), "alice", "se\ncret"u8, CancellationToken.None));
        Assert.Throws<ArgumentException>(() => new CredentialBroker(new("https://example.invalid/"), "alice", "se\rcret"u8, CancellationToken.None));
    }

    [Fact]
    public void WhenTheSecretHasANulCharacter_ShouldRefuseToCreateTheBroker() =>
        Assert.Throws<ArgumentException>(() => new CredentialBroker(new("https://example.invalid/"), "alice", "se\0cret"u8, CancellationToken.None));

    // The secret is a token, so the token limit applies: a thousand and twenty-four characters, printable ASCII.
    [Fact]
    public void WhenTheSecretIsOneCharacterLongerThanATokenMayBe_ShouldRefuseToCreateTheBroker() =>
        Assert.Throws<ArgumentException>(() => new CredentialBroker(new("https://example.invalid/"), "alice", Encoding.ASCII.GetBytes(new string('a', 1025)), CancellationToken.None));

    // A token is printable ASCII.
    [Fact]
    public void WhenTheSecretIsNotAscii_ShouldRefuseToCreateTheBroker() =>
        Assert.Throws<ArgumentException>(() => new CredentialBroker(new("https://example.invalid/"), "alice", "s\u00e9cret"u8, CancellationToken.None));

    [Fact]
    public async Task WhenTheTokenHasTheLongestAcceptedLength_ShouldAnswerWithIt()
    {
        var secret = Encoding.ASCII.GetBytes(new string('a', 1024));
        await using var broker = new CredentialBroker(new("https://example.invalid/"), "alice", secret, CancellationToken.None);
        Assert.Equal(secret, broker.Authorize(new(broker.OperationId, "Password for 'https://alice@example.invalid': ")));
    }

    [Fact]
    public async Task WhenAnOperationAsksFourTimes_ShouldAnswerEachTime()
    {
        await using var broker = Broker();
        for (var i = 0; i < 4; i++) Assert.NotNull(broker.Authorize(new(broker.OperationId, "Username for 'https://example.invalid': ")));
    }

    [Fact]
    public async Task WhenAnOperationAsksAFifthTime_ShouldAnswerNothing()
    {
        await using var broker = Broker();
        for (var i = 0; i < 4; i++) Assert.NotNull(broker.Authorize(new(broker.OperationId, "Username for 'https://example.invalid': ")));
        Assert.Null(broker.Authorize(new(broker.OperationId, "Username for 'https://example.invalid': ")));
    }

    // Update submodules and a clone's submodules run one git submodule update --init --recursive per top-level submodule,
    // with one broker bound to the host. Every repository that command clones is a git clone of its own that asks once: a
    // library with five nested dependencies on the same host asks six times, each for another path.
    [Fact]
    public async Task WhenEveryRepositoryOfARecursiveSubmoduleUpdateAsksOnce_ShouldAnswerEachOfThem()
    {
        await using var host = Broker();
        var paths = new[] { "lib/core.git", "lib/json.git", "lib/zlib.git", "lib/png.git", "lib/jpeg.git", "lib/fonts.git" };

        var answers = paths.Select(path => host.Authorize(new(host.OperationId, "Password for 'https://alice@example.invalid/" + path + "': "))).ToArray();

        Assert.All(answers, Assert.NotNull);
    }

    [Fact]
    public async Task WhenABrokerBoundToARepositoryIsAskedAboutIt_ShouldAnswer()
    {
        await using var bound = Broker("https://example.invalid/owner/repo.git");
        Assert.Equal("synthetic-value"u8.ToArray(), bound.Authorize(new(bound.OperationId, "Password for 'https://alice@example.invalid/owner/repo.git': ")));
    }

    [Fact]
    public async Task WhenABrokerBoundToARepositoryIsAskedAboutAnotherOne_ShouldAnswerNothing()
    {
        await using var bound = Broker("https://example.invalid/owner/repo.git");
        Assert.Equal("synthetic-value"u8.ToArray(), bound.Authorize(new(bound.OperationId, "Password for 'https://alice@example.invalid/owner/repo.git': ")));
        Assert.Null(bound.Authorize(new(bound.OperationId, "Password for 'https://alice@example.invalid/owner/other.git': ")));
    }

    [Fact]
    public async Task WhenABrokerBoundToARepositoryIsAskedAboutTheHostAlone_ShouldAnswerNothing()
    {
        await using var bound = Broker("https://example.invalid/owner/repo.git");
        Assert.Equal("synthetic-value"u8.ToArray(), bound.Authorize(new(bound.OperationId, "Password for 'https://alice@example.invalid/owner/repo.git': ")));
        Assert.Null(bound.Authorize(new(bound.OperationId, "Password for 'https://alice@example.invalid/owner/other.git': ")));
        Assert.Null(bound.Authorize(new(bound.OperationId, "Password for 'https://alice@example.invalid/': ")));
    }

    [Fact]
    public async Task WhenABrokerBoundToTheHostIsAskedAboutAnyPath_ShouldAnswer()
    {
        await using var root = Broker("https://example.invalid/");
        Assert.NotNull(root.Authorize(new(root.OperationId, "Password for 'https://alice@example.invalid/any/path.git': ")));
    }

    [Theory]
    [InlineData("Password for 'https://alice@example.invalid'")]
    [InlineData("Token for 'https://alice@example.invalid': ")]
    [InlineData("Username for 'https://alice@example.invalid': ")]
    [InlineData("Password for 'https://example.invalid': ")]
    [InlineData("Password for 'https://bob@example.invalid': ")]
    [InlineData("Password for 'https://alice@example.invalid:8443': ")]
    [InlineData("Password for 'https://alice@example.invalid/?x=1': ")]
    [InlineData("Password for 'http://alice@example.invalid': ")]
    [InlineData("Username for 'not a url': ")]
    public async Task WhenThePromptDoesNotMatchTheBoundIdentity_ShouldRefuseIt(string prompt)
    {
        await using var broker = Broker();
        Assert.Null(broker.Authorize(new(broker.OperationId, prompt)));
    }

    // The username prompt needs no user in the address.
    [Fact]
    public async Task WhenGitAsksForTheUsername_ShouldAnswerWithTheBoundUser()
    {
        await using var broker = Broker();
        Assert.Equal("alice"u8.ToArray(), broker.Authorize(new(broker.OperationId, "Username for 'https://example.invalid': ")));
    }

    [Fact]
    public async Task WhenGitAsksForThePasswordOfTheBoundUser_ShouldAnswerWithTheSecret()
    {
        await using var broker = Broker();
        Assert.Equal("alice"u8.ToArray(), broker.Authorize(new(broker.OperationId, "Username for 'https://example.invalid': ")));
        Assert.Equal("synthetic-value"u8.ToArray(), broker.Authorize(new(broker.OperationId, "Password for 'https://alice@example.invalid': ")));
    }

    [Fact]
    public async Task WhenThePasswordPromptWritesTheHostInCapitals_ShouldStillAnswerWithTheSecret()
    {
        await using var broker = Broker();
        Assert.Equal("alice"u8.ToArray(), broker.Authorize(new(broker.OperationId, "Username for 'https://example.invalid': ")));
        Assert.Equal("synthetic-value"u8.ToArray(), broker.Authorize(new(broker.OperationId, "Password for 'https://alice@example.invalid': ")));
        Assert.Equal("synthetic-value"u8.ToArray(), broker.Authorize(new(broker.OperationId, "Password for 'https://EXAMPLE.invalid': ".Replace("https://", "https://alice@"))));
    }

    [Fact]
    public async Task WhenTheBrokerIsDisposed_ShouldAnswerNothing()
    {
        var broker = Broker();
        await broker.DisposeAsync();
        Assert.Null(broker.Authorize(new(broker.OperationId, "Username for 'https://example.invalid': ")));
    }

    [Fact]
    public async Task WhenTheBrokerIsDisposedASecondTime_ShouldDoNothing()
    {
        var broker = Broker();
        await broker.DisposeAsync();
        Assert.Null(broker.Authorize(new(broker.OperationId, "Username for 'https://example.invalid': ")));
        await broker.DisposeAsync();
    }

    [Fact]
    public async Task WhenOutputCarriesTheTokenOrItsBasicAuthenticationForm_ShouldRedactBoth()
    {
        await using var broker = Broker();
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:synthetic-value"));
        Assert.Equal("token [credential] header [credential] end", broker.Redact("token synthetic-value header " + basic + " end"));
    }

    [Fact]
    public async Task WhenGitIsStartedThroughTheBroker_ShouldRouteItsPromptsToTragButNeverCarryTheSecret()
    {
        await using var broker = Broker();
        var environment = broker.CreateEnvironment("Trag.exe");
        Assert.Equal(Path.GetFullPath("Trag.exe"), environment["GIT_ASKPASS"]);
        Assert.Equal("1", environment["TRAG_ASKPASS"]); Assert.Equal(broker.PipeName, environment["TRAG_ASKPASS_PIPE"]); Assert.Equal(broker.OperationId, environment["TRAG_ASKPASS_OPERATION"]);
        Assert.Equal("0", environment["GIT_TERMINAL_PROMPT"]); Assert.Null(environment["GIT_CURL_VERBOSE"]);
        Assert.DoesNotContain(environment.Values, value => value?.Contains("synthetic", StringComparison.Ordinal) == true);
    }

    // Git skips certificate verification whenever GIT_SSL_NO_VERIFY exists, whatever its value, so a man in the middle
    // could take the token; a null entry also removes the variable a terminal passed on.
    [Fact]
    public async Task WhenGitIsStartedThroughTheBroker_ShouldRemoveTheVariableThatTurnsOffCertificateVerification()
    {
        await using var broker = Broker();
        Assert.Null(broker.CreateEnvironment("Trag.exe")["GIT_SSL_NO_VERIFY"]);
    }

    [Fact]
    public async Task WhenGitIsStartedThroughTheBroker_ShouldTieItsCredentialSettingsToTheOrigin()
    {
        await using var broker = Broker();
        Assert.Contains("credential.https://example.invalid/.helper=", broker.EndpointArguments);
        Assert.Contains("credential.https://example.invalid/.username=alice", broker.EndpointArguments);
        Assert.Contains("http.https://example.invalid/.sslVerify=true", broker.EndpointArguments);
    }

    [Fact]
    public void WhenGitIsStartedThroughTheBroker_ShouldIgnoreEveryConfiguredCredentialHelperAndRedirect()
    {
        Assert.Contains("credential.helper=", CredentialBroker.IsolatedHelperArguments);
        Assert.Contains("http.followRedirects=false", CredentialBroker.IsolatedHelperArguments);
    }

    [Fact]
    public async Task WhenABrokerIsCreated_ShouldNameItsPipeForTragAndGiveItsOperationAFullId()
    {
        await using var broker = Broker();
        Assert.StartsWith("trag-", broker.PipeName); Assert.Equal(32, broker.OperationId.Length);
    }

    [Fact]
    public async Task WhenAFrameIsWritten_ShouldPrefixItsLengthLittleEndian()
    {
        using var stream = new MemoryStream();
        await PipeFrames.WriteAsync(stream, "abc"u8.ToArray(), CancellationToken.None);
        Assert.Equal(new byte[] { 3, 0, 0, 0, (byte)'a', (byte)'b', (byte)'c' }, stream.ToArray());
    }

    [Fact]
    public async Task WhenAWrittenFrameIsRead_ShouldGiveBackItsBytes()
    {
        using var stream = new MemoryStream();
        await PipeFrames.WriteAsync(stream, "abc"u8.ToArray(), CancellationToken.None);
        stream.Position = 0;
        Assert.Equal("abc"u8.ToArray(), await PipeFrames.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task WhenAFrameIsLongerThanTheMaximum_ShouldRefuseToWriteIt() =>
        await Assert.ThrowsAsync<InvalidDataException>(() => PipeFrames.WriteAsync(new MemoryStream(), new byte[PipeFrames.MaximumLength + 1], CancellationToken.None));

    [Fact]
    public async Task WhenAFrameNamesANegativeLength_ShouldRefuseToReadIt() =>
        await Assert.ThrowsAsync<InvalidDataException>(() => PipeFrames.ReadAsync(new MemoryStream([0xFF, 0xFF, 0xFF, 0xFF]), CancellationToken.None));

    [Fact]
    public async Task WhenAFrameNamesALengthOverTheMaximum_ShouldRefuseToReadIt() =>
        await Assert.ThrowsAsync<InvalidDataException>(() => PipeFrames.ReadAsync(new MemoryStream([0x01, 0x40, 0, 0]), CancellationToken.None));

    [Fact]
    public async Task WhenTheStreamEndsInsideTheLength_ShouldSayItEnded() =>
        await Assert.ThrowsAsync<EndOfStreamException>(() => PipeFrames.ReadAsync(new MemoryStream([1, 0]), CancellationToken.None));

    [Fact]
    public async Task WhenTheStreamEndsBeforeTheWholeFrame_ShouldSayItEnded() =>
        await Assert.ThrowsAsync<EndOfStreamException>(() => PipeFrames.ReadAsync(new MemoryStream([5, 0, 0, 0, 1]), CancellationToken.None));

    [Fact]
    public async Task WhenTheAskpassClientNamesAnotherOperation_ShouldGetAnEmptyAnswerAndExitWithTwo()
    {
        await using var broker = Broker();
        using var output = new MemoryStream();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Equal(2, await AskPassClient.RunAsync(broker.PipeName, "other-operation", "Username for 'https://example.invalid': ", output, timeout.Token));
        Assert.Empty(output.ToArray());
    }

    // A process of this user that sends a broken frame, or a prompt Git never writes, ends only its own exchange: the
    // broker still answers Git afterwards and closes without an error of its own.
    [Fact]
    public async Task WhenAnExchangeIsBroken_ShouldKeepAnsweringAndCloseCleanly()
    {
        var broker = Broker();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using (var pipe = new NamedPipeClientStream(".", broker.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            await pipe.ConnectAsync(timeout.Token);
            await pipe.WriteAsync(new byte[] { 0xFF, 0xFF, 0xFF, 0x7F }, timeout.Token);
        }
        using var output = new MemoryStream();

        Assert.Equal(2, await AskPassClient.RunAsync(broker.PipeName, broker.OperationId, "Username for ': ", output, timeout.Token));
        Assert.Equal(0, await AskPassClient.RunAsync(broker.PipeName, broker.OperationId, "Username for 'https://example.invalid': ", output, timeout.Token));

        Assert.Equal("alice\n", Encoding.ASCII.GetString(output.ToArray()));
        await broker.DisposeAsync();
    }

    [Fact]
    public async Task WhenTheAskpassClientFindsNoPipe_ShouldExitWithTwo()
    {
        await using var broker = Broker();
        using var output = new MemoryStream();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Equal(2, await AskPassClient.RunAsync(broker.PipeName, "other-operation", "Username for 'https://example.invalid': ", output, timeout.Token));
        Assert.Equal(2, await AskPassClient.RunAsync("trag-no-such-pipe", broker.OperationId, "Username for 'https://example.invalid': ", output, new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token));
    }
}
