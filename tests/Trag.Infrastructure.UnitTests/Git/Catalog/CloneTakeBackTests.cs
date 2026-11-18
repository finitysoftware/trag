using Trag.Domain.Errors;
using Trag.Infrastructure.Git.Catalog;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Sync;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Catalog;

// A clone that fails after Git cloned it is reported as failed, so it is not listed; its folder goes, as it does for a
// clone Git itself stopped, or the same clone would be refused for a folder that is not empty.
public sealed class CloneTakeBackTests
{
    // The first Git after the clone meets an account that no longer answers, or the clone turns out no working copy.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenACloneFailsAfterGitClonedIt_ShouldTakeTheFolderBack(bool accountRefused)
    {
        using var source = await GitFixture.CreateAsync();
        await source.WriteAsync("a.txt", "one\n");
        await source.CommitAsync("first");
        var target = Path.Combine(source.Container, "clone");
        var exits = 0;
        var runner = new GitProcessRunner(exited: _ =>
        {
            ++exits;
            if (accountRefused && exits == 2) throw new AccountException("The token expired.");
            if (!accountRefused && exits == 1) Remove(Path.Combine(target, ".git"));
        });
        var clone = new GitCloneOperation(runner, new(runner), null, null, SyncTimeouts.Quiet);

        var result = await clone.CloneAsync(new(source.Root, target), CancellationToken.None);

        Assert.False(result.Succeeded, result.Message);
        Assert.False(Directory.Exists(target));
    }

    // Windows keeps a folder while a program holds one of its files (a virus scan, an indexer, an editor), and the same
    // clone would then be refused for a folder that is not empty, so the clone says what it left.
    [Fact]
    public async Task WhenAProgramStillHoldsAFileOfACanceledClone_ShouldSayWhatWasLeft()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var source = await GitFixture.CreateAsync();
        await source.WriteAsync("a.txt", "one\n");
        await source.CommitAsync("first");
        var target = Path.Combine(source.Container, "clone");
        using var cancel = new CancellationTokenSource();
        FileStream? held = null;
        var runner = new GitProcessRunner(exited: _ =>
        {
            if (held is not null) return;
            held = new FileStream(Path.Combine(target, "a.txt"), FileMode.Open, FileAccess.Read, FileShare.None);
            cancel.Cancel();
        });
        var clone = new GitCloneOperation(runner, new(runner), null, null, SyncTimeouts.Quiet);

        string message;
        try { message = (await clone.CloneAsync(new(source.Root, target), cancel.Token)).Message; }
        finally { held?.Dispose(); }

        Assert.Equal("Clone canceled. Trag could not remove what the clone wrote to " + target + ", perhaps because another program still holds its files. Delete it before you clone there again.",
            message);
    }

    // Git marks its objects read-only.
    private static void Remove(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(folder, true);
    }
}
