using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Write;

// Git takes a file whose size and modification time match its index entry as unchanged, and on Windows it compares that
// time to the second. A file rewritten with the same size within the second its entry records is therefore read only
// while the index is no newer than that second, and Git marks such an entry when it writes a newer index. A write
// through Trag's private index must leave the change as visible as it was.
public sealed class RacyIndexTests
{
    [Fact]
    public async Task WhenAnotherFileIsStagedWhileAChangeLooksUnchangedByItsTime_ShouldKeepTheChangeVisibleToGit()
    {
        using var f = await GitFixture.CreateAsync();
        await f.GitAsync("config", "core.autocrlf", "false");
        var sample = Path.Combine(f.Root, "sample.txt");
        var then = DateTime.UtcNow.AddMinutes(-1);
        await f.WriteAsync("sample.txt", "before\n"); File.SetLastWriteTimeUtc(sample, then);
        await f.WriteAsync("one.txt", "one\n");
        await f.CommitAsync("Base", "sample.txt", "one.txt");
        await f.WriteAsync("sample.txt", "change\n"); File.SetLastWriteTimeUtc(sample, then);
        File.SetLastWriteTimeUtc(Path.Combine(f.Root, ".git", "index"), then);
        await f.WriteAsync("one.txt", "one\nmore\n");
        var expected = await f.ReadAsync();
        Assert.Contains(expected.Files, file => file.Path.Display == "sample.txt" && file.IsUnstaged);

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new StageRequest(expected, [new("one.txt"u8.ToArray())]), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal("sample.txt", await f.GitAsync("diff", "--name-only"));
    }

    // Git writes the private index before the commit's hook runs and Trag publishes it after; an entry no older than the
    // private index stays marked only while the published index keeps that index's time. The hook gives the private index
    // the entry's own second, as if Git had written it then, so the test does not depend on how fast Git runs.
    [Fact]
    public async Task WhenTheIndexIsPublishedSecondsAfterGitWroteIt_ShouldKeepAChangeOfThatSecondVisibleToGit()
    {
        using var f = await GitFixture.CreateAsync();
        await f.GitAsync("config", "core.autocrlf", "false");
        var second = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();
        var then = DateTimeOffset.FromUnixTimeSeconds(second).UtcDateTime;
        var hooks = Path.Combine(f.Container, "hooks");
        Directory.CreateDirectory(hooks);
        var hook = Path.Combine(hooks, "pre-commit");
        await File.WriteAllTextAsync(hook, "#!/bin/sh\ntouch -d @" + second + " \"$GIT_INDEX_FILE\"\n", f.Token);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        await f.GitAsync("config", "core.hooksPath", hooks.Replace('\\', '/'));
        var file = Path.Combine(f.Root, "x.txt");
        await f.WriteAsync("x.txt", "one\n"); File.SetLastWriteTimeUtc(file, then);
        await f.GitAsync("add", "x.txt");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new CommitRequest(await f.ReadAsync(), "First"), f.Token);

        await f.WriteAsync("x.txt", "two\n"); File.SetLastWriteTimeUtc(file, then);
        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal("x.txt", await f.GitAsync("diff", "--name-only"));
    }
}
