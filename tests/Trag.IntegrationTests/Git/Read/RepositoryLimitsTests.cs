using System.Text;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// What the snapshot reader says when the repository itself is the obstacle: more output than the runner will hold, a
// preserved recovery index, or an identity Git refuses to build.
public sealed class RepositoryLimitsTests
{
    [Fact]
    public async Task WhenGitPrintsMoreThanTheRunnerHolds_ShouldReportARepositoryErrorNotCrash()
    {
        using var f = await GitFixture.CreateAsync();
        for (var i = 0; i < 20; i++) await File.WriteAllTextAsync(Path.Combine(f.Root, $"file-{i}.txt"), "content\n", f.Token);
        var reader = new CliRepositoryReader(new GitProcessRunner(outputLimit: 64));
        Assert.Equal(RepositoryError.OutputTooLarge, (await Assert.ThrowsAsync<RepositoryException>(() => reader.ReadAsync(f.Root, f.Token))).Error);
    }

    // A large repository: its index, its attributes and its refs are read a record at a time, so none of them has to fit
    // the runner's limit, and an LFS file at the end of the index is still found.
    [Fact]
    public async Task WhenTheIndexAndTheRefsPrintMoreThanTheRunnerHolds_ShouldStillReadTheRepository()
    {
        using var f = await GitFixture.CreateAsync();
        await LfsConfig.SetAsync(f, f.Root, installed: false);
        var names = Enumerable.Range(0, 150).Select(i => $"file-{i:000}.txt").ToArray();
        foreach (var name in names) await f.WriteAsync(name, "content\n");
        var head = await f.CommitAsync("Files", names);
        await f.WriteAsync(".gitattributes", "file-149.txt filter=lfs\n");
        await f.CommitAsync("Attributes", ".gitattributes");
        var tags = string.Concat(Enumerable.Range(0, 150).Select(i => $"create refs/tags/tag-{i:000} {head}\n"));
        await f.Runner.RunAsync(f.Root, ["update-ref", "--stdin"], f.Token, standardInput: Encoding.ASCII.GetBytes(tags));

        var snapshot = await new CliRepositoryReader(new GitProcessRunner(outputLimit: 4096)).ReadAsync(f.Root, f.Token);

        Assert.Contains(LimitationText.LfsNotInstalled, snapshot.Limitations);
        Assert.Equal((await f.ReadAsync()).TagRefs, snapshot.TagRefs);
    }

    [Fact]
    public async Task WhenATragRecoveryIndexIsLeftBehind_ShouldReportItUntilItIsMovedOutButNotWhileALiveWriteHoldsTheLock()
    {
        using var f = await GitFixture.CreateAsync();
        await f.GitAsync("commit", "--allow-empty", "-m", "Initial fixture");
        var gitDirectory = Path.Combine(f.Root, ".git");
        var recovery = Path.Combine(gitDirectory, "trag-write-0123456789abcdef.index");
        var lockFile = Path.Combine(gitDirectory, "index.lock");
        await File.WriteAllBytesAsync(recovery, [1, 2, 3], f.Token);
        Assert.Contains((await f.ReadAsync()).Limitations, value => value.StartsWith("A Trag recovery index", StringComparison.Ordinal));
        // A live write holds index.lock open, as Trag's own does: its private index is work in progress, not recovery work.
        using (new FileStream(lockFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            Assert.DoesNotContain((await f.ReadAsync()).Limitations, value => value.StartsWith("A Trag recovery index", StringComparison.Ordinal));
        File.Delete(lockFile);
        File.Delete(recovery);
        Assert.DoesNotContain((await f.ReadAsync()).Limitations, value => value.StartsWith("A Trag recovery index", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenAPartialCloneIsConfiguredEitherWay_ShouldReportIt()
    {
        using var f = await GitFixture.CreateAsync();
        await f.GitAsync("commit", "--allow-empty", "-m", "Initial fixture");
        Assert.DoesNotContain((await f.ReadAsync()).Limitations, value => value.StartsWith("Partial clone", StringComparison.Ordinal));

        await f.GitAsync("config", "extensions.partialclone", "origin");
        Assert.Contains("Partial clone: repository writes are not supported yet.", (await f.ReadAsync()).Limitations);
        await f.GitAsync("config", "--unset", "extensions.partialclone");

        // A promisor remote says the same thing, through the remote rather than through the repository extensions.
        await f.GitAsync("config", "remote.origin.url", "https://example.invalid/repo.git");
        await f.GitAsync("config", "remote.origin.promisor", "true");
        Assert.Contains("Partial clone: repository writes are not supported yet.", (await f.ReadAsync()).Limitations);
    }

    // A hand-edited configuration may hold an empty value, false to Git, or a key alone, true to Git; the warnings read
    // core.sparseCheckout and a remote's promisor the same way.
    [Fact]
    public async Task WhenSparseCheckoutOrAPromisorIsEmptyOrHasNoValue_ShouldReadItAsGitDoes()
    {
        using var f = await GitFixture.CreateAsync();
        await f.GitAsync("commit", "--allow-empty", "-m", "Initial fixture");
        await f.GitAsync("config", "remote.origin.url", "https://example.invalid/repo.git");
        var config = Path.Combine(f.Root, ".git", "config");
        await File.AppendAllTextAsync(config, "[core]\n\tsparseCheckout =\n[remote \"origin\"]\n\tpromisor =\n", f.Token);

        var empty = (await f.ReadAsync()).Limitations;
        await File.AppendAllTextAsync(config, "[core]\n\tsparseCheckout\n[remote \"origin\"]\n\tpromisor\n", f.Token);
        var alone = (await f.ReadAsync()).Limitations;

        Assert.DoesNotContain(LimitationText.SparseCheckout, empty);
        Assert.DoesNotContain(LimitationText.PartialClone, empty);
        Assert.Contains(LimitationText.SparseCheckout, alone);
        Assert.Contains(LimitationText.PartialClone, alone);
    }

    // The index questions cost two Git processes over the whole index; one reader asks them again only for a changed index.
    [Fact]
    public async Task WhenTheSameReaderReadsAgain_ShouldAskTheIndexQuestionsAgainOnlyIfTheIndexChanged()
    {
        using var f = await GitFixture.CreateAsync();
        await LfsConfig.SetAsync(f, f.Root, installed: false);
        await f.WriteAsync("a.txt", "a\n");
        var head = await f.CommitAsync("Initial fixture", "a.txt");
        var processes = 0;
        var reader = new CliRepositoryReader(new GitProcessRunner(exited: _ => Interlocked.Increment(ref processes)));
        Assert.DoesNotContain(LimitationText.LfsNotInstalled, (await reader.ReadAsync(f.Root, f.Token)).Limitations);
        var first = processes;
        processes = 0;
        Assert.DoesNotContain(LimitationText.LfsNotInstalled, (await reader.ReadAsync(f.Root, f.Token)).Limitations);
        var unchanged = processes;
        Assert.Equal(unchanged + 4, first); // --version, var GIT_ATTR_GLOBAL, ls-files and check-attr
        await f.WriteAsync(".gitattributes", "a.txt filter=lfs\n");
        await f.GitAsync("update-index", "--add", ".gitattributes");
        processes = 0;
        Assert.Contains(LimitationText.LfsNotInstalled, (await reader.ReadAsync(f.Root, f.Token)).Limitations);
        Assert.Equal(unchanged + 2, processes); // ls-files and check-attr
    }

    // check-attr --cached takes attributes from the index, but also from info/attributes and from the global attributes
    // file, here the one core.attributesFile names, so LFS set up there without an index change is seen on the next read.
    // A relative core.attributesFile is the one Git finds from the top of the working tree.
    [Theory]
    [InlineData("info/attributes")]
    [InlineData("attributes file")]
    [InlineData("relative core.attributesFile")]
    [InlineData("core.attributesFile")]
    public async Task WhenLfsIsAddedThroughInfoAttributesOrCoreAttributesFile_ShouldInvalidateTheCachedAnswer(string where)
    {
        using var f = await GitFixture.CreateAsync();
        await LfsConfig.SetAsync(f, f.Root, installed: false);
        var global = Path.Combine(f.Container, "attributes");
        if (where != "core.attributesFile")
            await f.GitAsync("config", "core.attributesFile", where == "relative core.attributesFile" ? "../attributes" : global);
        await f.WriteAsync("asset.bin", "asset\n");
        await f.CommitAsync("Asset", "asset.bin");
        var reader = new CliRepositoryReader(new GitProcessRunner());
        Assert.Empty((await reader.ReadAsync(f.Root, f.Token)).Limitations);
        await File.WriteAllTextAsync(where == "info/attributes" ? Path.Combine(f.Root, ".git", "info", "attributes") : global, "*.bin filter=lfs\n", f.Token);
        if (where == "core.attributesFile") await f.GitAsync("config", "core.attributesFile", global);

        Assert.Equal([LimitationText.LfsNotInstalled], (await reader.ReadAsync(f.Root, f.Token)).Limitations);
    }

    // Without Git LFS, Git would commit an LFS file itself instead of its pointer, so a repository with LFS files is
    // writable only where Git LFS is installed.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenTheIndexHoldsLfsFiles_ShouldLimitTheRepositoryOnlyWithoutGitLfs(bool installed)
    {
        using var f = await GitFixture.CreateAsync();
        await LfsConfig.SetAsync(f, f.Root, installed);
        await f.WriteAsync("asset.bin", "asset\n");
        await f.CommitAsync("Asset", "asset.bin");
        await f.WriteAsync(".gitattributes", "*.bin filter=lfs\n");
        await f.CommitAsync("Attributes", ".gitattributes");

        var snapshot = await f.ReadAsync();

        Assert.Equal(installed ? [] : [LimitationText.LfsNotInstalled], snapshot.Limitations);
    }

    [Fact]
    public async Task WhenGitRefusesToBuildTheIdentity_ShouldReadNoIdentity()
    {
        using var f = await GitFixture.CreateAsync();
        await f.GitAsync("commit", "--allow-empty", "-m", "Initial fixture");
        var known = await f.ReadAsync();
        Assert.Equal("Trag Fixture", known.Author!.Name);
        Assert.Equal("fixture@example.invalid", known.Committer!.Email);
        await f.GitAsync("config", "user.name", "");
        var unknown = await f.ReadAsync();
        Assert.Null(unknown.Author);
        Assert.Null(unknown.Committer);
    }
}
