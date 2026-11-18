using Trag.Infrastructure.Git.Diagnostics;

namespace Trag.Infrastructure.UnitTests.Git.Diagnostics;

public sealed class GitOutputRedactionTests
{
    [Fact]
    public void WhenAHookFails_ShouldShowItsMessageBoundedWithoutSecretsOrTerminalControlSequences()
    {
        var result = GitOutputRedaction.Redact("Failed https://user:password@example.invalid/repo?token=value\nAuthorization: Bearer private-value\nToken=another-secret\nghp_123456789\n[31mHook rejected\n" + new string('x', 20_000));
        Assert.DoesNotContain("password", result);
        Assert.DoesNotContain("private-value", result);
        Assert.DoesNotContain("another-secret", result);
        Assert.DoesNotContain("ghp_123456789", result);
        Assert.DoesNotContain('', result);
        Assert.Contains("Hook rejected", result);
        Assert.True(result.Length <= 8192);
    }

    // Every secret is replaced by a marker longer than itself, so the text grows past the bound only after redaction.
    [Fact]
    public void WhenRedactionMakesTheTextLonger_ShouldStillBoundIt() =>
        Assert.True(GitOutputRedaction.Redact(string.Concat(Enumerable.Repeat("token=x\n", 2000))).Length <= 8192);

    // Git quotes the address it could not use and ends the sentence after it. The quote and the punctuation are the
    // sentence's, not the address's; swallowing them hides the host the user needs to recognise.
    [Fact]
    public void WhenAGitErrorQuotesAnAddress_ShouldKeepItsHostAndLeaveTheQuoteToTheSentence() =>
        Assert.Equal("fatal: could not read Username for 'https://github.com/': terminal prompts disabled",
            GitOutputRedaction.Redact("fatal: could not read Username for 'https://github.com': terminal prompts disabled"));

    [Fact]
    public void WhenAQuotedAddressCarriesCredentials_ShouldRemoveThemAndKeepTheHostAndTheQuote() =>
        Assert.Equal("fatal: unable to access 'https://example.invalid/repo.git/': Could not resolve host",
            GitOutputRedaction.Redact("fatal: unable to access 'https://user:secret@example.invalid/repo.git/': Could not resolve host"));

    // The period is the sentence's, not the address's.
    [Fact]
    public void WhenASentenceEndsRightAfterAnAddress_ShouldDropTheQueryAndKeepThePeriod() =>
        Assert.Equal("See https://example.invalid/repo.", GitOutputRedaction.Redact("See https://example.invalid/repo?token=dropped."));

    [Fact]
    public void WhenAPushPrintsProgressAndTooMuchChatter_ShouldCollapseTheProgressAndKeepTheFinalErrorAfterTheCut()
    {
        var progress = string.Concat(Enumerable.Range(1, 700).Select(i => "Writing objects:  " + i / 7 + "% (" + i + "/716)\r"));
        var text = progress + "Writing objects: 100% (716/716), 1.20 MiB | 2.00 MiB/s, done.\nremote: error: GH013: Repository rule violations found\r\n"
            + "error: failed to push some refs to 'https://github.com/finitysoftware/trag.git'\n" + string.Concat(Enumerable.Repeat("remote: chatter line\n", 800)) + "send-pack: unexpected disconnect while reading sideband packet\n";
        var result = GitOutputRedaction.Redact(text);
        Assert.DoesNotContain("(1/716)", result);
        Assert.Contains("Writing objects: 100% (716/716)", result);
        Assert.Contains("GH013", result);
        Assert.Contains("characters omitted", result);
        Assert.EndsWith("send-pack: unexpected disconnect while reading sideband packet\n", result);
        Assert.True(result.Length <= 8192);
    }

    // A terminal rewrites a progress line in place, so only its last state is what the user saw.
    [Fact]
    public void WhenALineIsRewrittenWithCarriageReturns_ShouldKeepOnlyItsLastState() =>
        Assert.Equal("done.\nnext", GitOutputRedaction.Redact("10%\r50%\rdone.\r\nnext"));

    // An scp-like address Git quotes in its error keeps its host and path, never a password before them.
    [Fact]
    public void WhenAnScpAddressCarriesAPassword_ShouldDropTheUserInfo()
    {
        var result = GitOutputRedaction.Redact("fatal: 'deploy:hunter2@example.invalid:team/repo.git' does not appear to be a git repository");
        Assert.DoesNotContain("hunter2", result);
        Assert.Contains("example.invalid:team/repo.git", result);
    }

    // A file address with a space keeps its whole path: nothing in it is a credential.
    [Fact]
    public void WhenAFileUrlHasSpaces_ShouldKeepThePath() =>
        Assert.Equal("fatal: 'file:///D:/My Repos/team.git' does not appear to be a git repository",
            GitOutputRedaction.Redact("fatal: 'file:///D:/My Repos/team.git' does not appear to be a git repository"));

    [Fact]
    public void WhenTheTextIsExactly8192Characters_ShouldReturnItWhole()
    {
        var text = new string('x', 8192);
        Assert.Equal(text, GitOutputRedaction.Redact(text));
    }

    // Output far past the bound is cut down to its first 8,192 and last 24,576 characters. A hook that prints a long secret
    // shrinks to the marker once hidden, which brings that cut into the text shown, so a secret the cut runs through must
    // not survive in either part.
    [Theory]
    [InlineData("Authorization: Bearer ", "private-bearer-value", "", true)]
    [InlineData("fatal: unable to access 'https://deploy:", "hunter2-password", "@example.invalid/repo.git/'", true)]
    [InlineData("fatal: 'deploy:", "hunter2-password", "@example.invalid:team/repo.git' does not appear to be a git repository", false)]
    public void WhenATokenStraddlesTheCut_ShouldLeaveNoFragment(string before, string secret, string after, bool inTheTail)
    {
        var line = before + secret + after + "\n";
        var middle = before.Length + secret.Length / 2;
        var chatter = string.Concat(Enumerable.Repeat("remote: chatter line\n", 2_000));
        var longSecret = "remote: token=" + new string('k', 30_000) + "\n";
        const string error = "fatal: the remote end hung up unexpectedly\n";
        var text = inTheTail
            ? chatter + line + longSecret[..(24_576 - (line.Length - middle) - error.Length - 1)] + "\n" + error
            : longSecret[..(8_192 - middle - 1)] + "\n" + line + chatter;
        var result = GitOutputRedaction.Redact(text);
        Assert.DoesNotContain(secret[(secret.Length / 2)..], result);
        Assert.DoesNotContain(secret[..(secret.Length / 2)], result);
    }

    // Output that is one long line has no line break for the first cut to keep: it keeps the head as it was, so a secret
    // the tail cut runs through stays in the part the second cut leaves out.
    [Fact]
    public void WhenTheOutputIsOneLongLine_ShouldNotShowAPieceOfASecretTheTailCutRunsThrough()
    {
        const string address = " https://deploy:hunter2-password@example.invalid/repo.git ";
        var cut = 40_000 - 24_576;
        var before = new string('x', cut - " https://deploy:hunter2-".Length);
        var text = before + address + new string('y', 40_000 - before.Length - address.Length);
        var result = GitOutputRedaction.Redact(text);
        Assert.DoesNotContain("hunter2", result);
        Assert.DoesNotContain("password@", result);
    }

    [Fact]
    public void WhenTheFirstLineIsLongerThanTheHead_ShouldStillShowTheStartOfTheOutput()
    {
        var text = "fatal: start of the output " + new string('a', 9_000) + "\n" + string.Concat(Enumerable.Repeat("remote: chatter line\n", 2_000));
        Assert.StartsWith("fatal: start of the output", GitOutputRedaction.Redact(text));
    }
}
