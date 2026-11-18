using Trag.Domain.Blame;
using Trag.Domain.Diff;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Shapes;

// A clone made with --filter=blob:none holds only the file versions its checkout needed; Git fetches any other from the
// remote the moment a command asks for it. Trag reads with lazy fetching turned off, so a version that is not here stays
// on the remote. What is missing is asked with rev-list --missing=print, which fetches nothing itself.
public sealed class PartialCloneTests
{
    // The source answers filtered requests; a client's -c does not reach the upload-pack of a local transport.
    private static async Task<string> PartialCloneAsync(GitFixture f)
    {
        await f.GitAsync("config", "uploadpack.allowFilter", "true");
        var clone = Path.Combine(f.Container, "partial");
        await f.GitAtAsync(f.Container, "clone", "--filter=blob:none", "--", new Uri(f.Root).AbsoluteUri, clone);
        return clone;
    }

    private static async Task<string[]> MissingAsync(GitFixture f, string clone) =>
        [.. (await f.GitAtAsync(clone, "rev-list", "--objects", "--missing=print", "--all")).Split('\n')
            .Where(line => line.StartsWith('?')).Select(line => line[1..]).Order(StringComparer.Ordinal)];

    // A commit's diff needs the version before it, which the clone did not download. Reading it fetches nothing, while
    // plain Git in the same place fetches that version the moment it is asked for, so the clone could reach its remote.
    [Fact]
    public async Task WhenABlobIsMissingLocally_ShouldNotFetchItSilentlyForADiff()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("a.txt", "one\n"); await f.CommitAsync("One");
        await f.WriteAsync("a.txt", "one\ntwo\n"); var head = await f.CommitAsync("Two");
        var clone = await PartialCloneAsync(f);
        var before = await f.GitAtAsync(clone, "rev-parse", "HEAD~1:a.txt");
        Assert.Equal([before], await MissingAsync(f, clone));
        var history = new CliHistoryReader(f.Runner);

        var details = await history.ReadCommitAsync(clone, head, f.Token);
        await history.ReadDiffAsync(clone, details, Assert.Single(details.Files), false, f.Token);

        Assert.Equal([before], await MissingAsync(f, clone));
        await f.GitAtAsync(clone, "cat-file", "-e", before);
        Assert.Empty(await MissingAsync(f, clone));
    }

    // A user who allows the remote's protocol by name, as protocol.file.allow=always for submodules on local paths, still
    // gets no fetch from a read: Git before 2.45 knows no GIT_NO_LAZY_FETCH, and protocol.<name>.allow beats protocol.allow.
    [Fact]
    public async Task WhenTheUserAllowsTheRemotesProtocolByName_ShouldStillNotFetchAMissingVersionForADiff()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("a.txt", "one\n"); await f.CommitAsync("One");
        await f.WriteAsync("a.txt", "one\ntwo\n"); var head = await f.CommitAsync("Two");
        var clone = await PartialCloneAsync(f);
        await f.GitAtAsync(clone, "config", "protocol.file.allow", "always");
        var before = await f.GitAtAsync(clone, "rev-parse", "HEAD~1:a.txt");
        var history = new CliHistoryReader(f.Runner);
        var details = await history.ReadCommitAsync(clone, head, f.Token);

        var diff = await history.ReadDiffAsync(clone, details, Assert.Single(details.Files), false, f.Token);

        Assert.Equal([before], await MissingAsync(f, clone));
        Assert.Equal((DiffContentKind.Unavailable, FileDiffText.NotDownloaded), (diff.Kind, diff.Message));
    }

    // Git answers a version that is not here as missing, not with a size: the diff says the content was not downloaded
    // instead of offering a preview of a large file.
    [Fact]
    public async Task WhenACommitDiffNeedsAVersionThatIsNotDownloaded_ShouldSaySoInsteadOfOfferingALargeFilePreview()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("a.txt", "one\n"); await f.CommitAsync("One");
        await f.WriteAsync("a.txt", "one\ntwo\n"); var head = await f.CommitAsync("Two");
        var clone = await PartialCloneAsync(f);
        var history = new CliHistoryReader(f.Runner);
        var details = await history.ReadCommitAsync(clone, head, f.Token);

        var diff = await history.ReadDiffAsync(clone, details, Assert.Single(details.Files), false, f.Token);

        Assert.Equal((DiffContentKind.Unavailable, FileDiffText.NotDownloaded, false), (diff.Kind, diff.Message, diff.CanPreview));
        Assert.Empty(diff.Lines);
    }

    // The index holds the newer version and HEAD the one that was never downloaded, as after a soft reset.
    [Fact]
    public async Task WhenAStagedDiffNeedsAVersionThatIsNotDownloaded_ShouldSaySoInsteadOfOfferingALargeFilePreview()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("a.txt", "one\n"); await f.CommitAsync("One");
        await f.WriteAsync("a.txt", "one\ntwo\n"); await f.CommitAsync("Two");
        var clone = await PartialCloneAsync(f);
        await f.GitAtAsync(clone, "reset", "--soft", "HEAD~1");
        var file = Assert.Single((await f.ReadAsync(clone)).Files);

        var diff = await new CliFileDiffReader(f.Runner).ReadAsync(clone, file, true, false, f.Token);

        Assert.Equal((DiffContentKind.Unavailable, FileDiffText.NotDownloaded, false), (diff.Kind, diff.Message, diff.CanPreview));
        Assert.Empty(diff.Lines);
    }

    // A file renamed and changed in one commit is matched to its old name by comparing both versions, and neither was
    // downloaded: the commit still reads, with the rename listed as the deletion and the addition Git records.
    [Fact]
    public async Task WhenACommitRenamesAFileWhoseOldVersionIsNotDownloaded_ShouldStillListItsFiles()
    {
        var lines = string.Concat(Enumerable.Range(1, 40).Select(number => number + "\n"));
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("a.txt", lines); await f.CommitAsync("One");
        await f.GitAsync("mv", "a.txt", "c.txt"); await f.WriteAsync("c.txt", lines + "41\n"); var renamed = await f.CommitAsync("Rename");
        await f.WriteAsync("c.txt", lines + "41\n42\n"); await f.CommitAsync("Three");
        var clone = await PartialCloneAsync(f);

        var details = await new CliHistoryReader(f.Runner).ReadCommitAsync(clone, renamed, f.Token);

        Assert.Equal((renamed, "Rename"), (details.Commit.Id, details.Commit.Subject));
        Assert.Equal([("a.txt", "D"), ("c.txt", "A")], details.Files.Select(file => (file.Path.Display, file.Status)));
    }

    // merge-tree needs the versions both branches changed, which the clone did not download, so it cannot answer; the
    // branches still share a commit, and the preview leaves the conflicts unknown instead of saying Git will not merge them.
    [Fact]
    public async Task WhenMergeTreeCannotReadAVersionThePartialCloneLacks_ShouldLeaveTheConflictsUnknownWithoutSayingTheBranchesShareNoCommit()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("f.txt", "base\n"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "topic");
        await f.WriteAsync("f.txt", "topic\n"); await f.CommitAsync("Topic");
        await f.GitAsync("switch", "main");
        await f.WriteAsync("f.txt", "main\n"); await f.CommitAsync("Main");
        var clone = await PartialCloneAsync(f);

        var preview = await new CliBranchReader(f.Runner).PreviewMergeAsync(clone, "refs/heads/main", "refs/remotes/origin/topic", f.Token);

        Assert.Equal((null, false), (preview.Conflicts, preview.SharesNoCommit));
    }

    // Blaming the file walks back to the version that is not here: the blame fails rather than fetch it.
    [Fact]
    public async Task WhenABlameNeedsAVersionThatIsNotDownloaded_ShouldFailWithoutFetchingIt()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("a.txt", "one\n"); await f.CommitAsync("One");
        await f.WriteAsync("a.txt", "one\ntwo\n"); await f.CommitAsync("Two");
        var clone = await PartialCloneAsync(f);
        var before = await f.GitAtAsync(clone, "rev-parse", "HEAD~1:a.txt");
        Assert.Equal([before], await MissingAsync(f, clone));

        await Assert.ThrowsAsync<GitCommandException>(() => new CliHistoryReader(f.Runner).ReadBlameAsync(clone, null, "a.txt", BlameOptions.None, f.Token));

        Assert.Equal([before], await MissingAsync(f, clone));
    }
}
