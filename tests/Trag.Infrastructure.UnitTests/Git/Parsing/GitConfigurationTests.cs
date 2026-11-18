using System.Text;
using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.UnitTests.Git.Parsing;

// A boolean in Git's configuration as git config --null lists it: a key alone is true and an empty value false, the
// words and any whole number but zero are true, and the last of several wins.
public sealed class GitConfigurationTests
{
    private static GitConfiguration Of(string listed) => new(GitConfigParser.Parse(Encoding.UTF8.GetBytes(listed)));

    // git config --null --get-regexp ^remote\. as Git 2.55 lists two remotes, the first with every transport setting Trag
    // passes on and two fetch refspecs.
    private const string Remotes = "remote.origin.url\nhttps://example.com/team/repo.git\0remote.origin.fetch\n+refs/heads/*:refs/remotes/origin/*\0"
        + "remote.origin.fetch\n+refs/tags/v1:refs/tags/v1\0remote.origin.receivepack\ngit-receive-pack\0remote.origin.tagopt\n--no-tags\0remote.origin.prune\ntrue\0remote.origin.proxy\nhttp://proxy.example:8080\0"
        + "remote.origin.proxyauthmethod\nbasic\0remote.origin.lfsurl\nhttps://lfs.example.com/team/repo\0"
        + "remote.origin.lfspushurl\nhttps://lfs-push.example.com/team/repo\0remote.upstream.url\nhttps://example.com/up/repo.git\0"
        + "remote.upstream.proxy\nhttp://other.example:3128\0";

    [Theory]
    [InlineData("commit.gpgsign\0", true)]
    [InlineData("commit.gpgsign\n\0", false)]
    [InlineData("commit.gpgsign\ntrue\0", true)]
    [InlineData("commit.gpgsign\nYes\0", true)]
    [InlineData("commit.gpgsign\non\0", true)]
    [InlineData("commit.gpgsign\n2\0", true)]
    [InlineData("commit.gpgsign\n-1\0", true)]
    [InlineData("commit.gpgsign\n00\0", false)]
    [InlineData("commit.gpgsign\noff\0", false)]
    [InlineData("commit.gpgsign\ntrue\0commit.gpgsign\nfalse\0", false)]
    [InlineData("tag.gpgsign\ntrue\0", false)]
    [InlineData("", false)]
    public void WhenAFlagIsRead_ShouldFollowGitsRuleForABoolean(string listed, bool expected) =>
        Assert.Equal(expected, Of(listed).Flag("commit.gpgsign"));

    // The other readers still see a key alone as an empty value.
    [Fact]
    public void WhenAKeyHasNoValue_ShouldListItWithAnEmptyValue() => Assert.Equal([("core.sparsecheckout", "")], Of("core.sparsecheckout\0").Entries);

    // The newline after the key splits each record, even with nothing before it.
    [Fact]
    public void WhenARecordStartsWithItsNewline_ShouldReadAnEmptyKeyWithTheValue() => Assert.Equal([("", "value")], Of("\nvalue\0").Entries);

    [Fact]
    public void WhenAKeyIsListedSeveralTimes_ShouldGiveEveryValueInOrder() =>
        Assert.Equal(["+refs/heads/*:refs/remotes/origin/*", "+refs/tags/v1:refs/tags/v1"], Of(Remotes).Values("remote.origin.fetch"));

    [Fact]
    public void WhenARemotesTransportSettingsAreRead_ShouldGiveItsOwnSevenWithoutThePrefix() =>
        Assert.Equal([("receivepack", "git-receive-pack"), ("tagopt", "--no-tags"), ("prune", "true"), ("proxy", "http://proxy.example:8080"), ("proxyauthmethod", "basic"),
            ("lfsurl", "https://lfs.example.com/team/repo"), ("lfspushurl", "https://lfs-push.example.com/team/repo")], Of(Remotes).TransportOptions("origin"));

    // Git LFS counts as installed when its filter process names a program, of any length; git config filter.lfs.process ""
    // leaves an empty one, which Git 2.55 lists as below.
    [Theory]
    [InlineData("filter.lfs.process\ngit-lfs filter-process\0", true)]
    [InlineData("filter.lfs.process\nx\0", true)]
    [InlineData("filter.lfs.process\n\0", false)]
    [InlineData("", false)]
    public void WhenTheLfsFilterProcessIsRead_ShouldCountLfsAsInstalledOnlyWhenItNamesAProgram(string listed, bool installed) =>
        Assert.Equal(installed, Of(listed).LfsInstalled);
}
