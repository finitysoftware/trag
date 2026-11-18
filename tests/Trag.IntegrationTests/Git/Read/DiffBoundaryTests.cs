using System.Diagnostics;
using Trag.Domain.Diff;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

public sealed class DiffBoundaryTests
{
    [Fact]
    public async Task WhenTheFileLiesBehindADirectoryLinkOutsideTheWorktree_ShouldRefuseThePreviewWithoutFollowingTheLink()
    {
        using var fixture = await GitFixture.CreateAsync();
        var outside = Path.Combine(fixture.Container, "outside");
        Directory.CreateDirectory(outside);
        var secret = Path.Combine(outside, "file.txt");
        await File.WriteAllTextAsync(secret, "outside content");
        var link = Path.Combine(fixture.Root, "linked");
        await CreateDirectoryLinkAsync(link, outside);
        try
        {
            var reader = new CliFileDiffReader(new(executable: new() { Path = "trag-missing-git" }));
            var file = new FileStatus(new("linked/file.txt"u8.ToArray()), '?', '?');
            var result = await reader.ReadAsync(fixture.Root, file, false, true, fixture.Token);
            Assert.Equal(DiffContentKind.Unavailable, result.Kind);
            Assert.Contains("Symbolic link or junction", result.Message);
            Assert.Empty(result.Lines);
            Assert.Equal("outside content", await File.ReadAllTextAsync(secret));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public async Task WhenAGitFilterDoesNotFinish_ShouldEndTheDiffReadOnItsOwnDeadlineAsATimeout()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("file.txt", "before\n");
        await fixture.CommitAsync("initial");
        await fixture.WriteAsync(".gitattributes", "file.txt filter=slow\n");
        await fixture.WriteAsync("file.txt", "changed content\n");
        await fixture.GitAsync("config", "filter.slow.clean", "sleep 60; cat");
        var reader = new CliFileDiffReader(fixture.Runner);
        var file = new FileStatus(new("file.txt"u8.ToArray()), '.', 'M');
        var reading = reader.ReadAsync(fixture.Root, file, false, false, fixture.Token);
        Assert.Same(reading, await Task.WhenAny(reading, Task.Delay(TimeSpan.FromSeconds(35), fixture.Token)));
        await Assert.ThrowsAsync<TimeoutException>(() => reading);
        Assert.False(fixture.Token.IsCancellationRequested);
        Assert.Equal("changed content\n", await File.ReadAllTextAsync(Path.Combine(fixture.Root, "file.txt")));
    }

    // Both sides fit the byte limit, but the patch of a file whose every line changed holds both of them and does not: the
    // diff offers the preview instead of failing on the cut patch.
    [Fact]
    public async Task WhenThePatchExceedsFiveMegabytesWhileTheFileIsSmaller_ShouldOfferThePreview()
    {
        using var fixture = await GitFixture.CreateAsync();
        static string Content(char letter) => string.Concat(Enumerable.Repeat(new string(letter, 999) + "\n", DiffLimits.ByteLimit / 1000 * 3 / 5));
        await fixture.WriteAsync("big.txt", Content('a'));
        await fixture.CommitAsync("first", "big.txt");
        await fixture.WriteAsync("big.txt", Content('b'));
        var file = Assert.Single((await fixture.ReadAsync()).Files);
        var reader = new CliFileDiffReader(fixture.Runner);

        var offered = await reader.ReadAsync(fixture.Root, file, false, false, fixture.Token);
        var preview = await reader.ReadAsync(fixture.Root, file, false, true, fixture.Token);

        Assert.True(new FileInfo(Path.Combine(fixture.Root, "big.txt")).Length < DiffLimits.ByteLimit);
        Assert.Equal((DiffContentKind.Limited, true), (offered.Kind, offered.CanPreview));
        Assert.Equal((DiffContentKind.Limited, DiffLimits.PreviewLines), (preview.Kind, preview.Lines.Count));
    }

    // The index holds a version too large for a whole diff and the working file is small again: the diff offers the
    // preview of what the working tree holds.
    [Fact]
    public async Task WhenTheIndexBlobIsLargeAndTheWorkingFileSmall_ShouldOfferAPreviewOfTheWorkingFile()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("big.txt", string.Concat(Enumerable.Repeat("line\n", DiffLimits.ByteLimit / 5 + 1)));
        await fixture.CommitAsync("first", "big.txt");
        await fixture.WriteAsync("big.txt", "small\n");
        var file = Assert.Single((await fixture.ReadAsync()).Files);
        var reader = new CliFileDiffReader(fixture.Runner);

        var offered = await reader.ReadAsync(fixture.Root, file, false, false, fixture.Token);
        var preview = await reader.ReadAsync(fixture.Root, file, false, true, fixture.Token);

        Assert.Equal((FileDiffText.LargeFileOffer, true), (offered.Message, offered.CanPreview));
        Assert.Equal(["small"], preview.Lines.Select(line => line.Text[1..]));
    }

    // A diff of exactly as many rows as the limit allows is shown whole, as the file is while it is untracked; the
    // headers Git prints before the rows are not rows of the diff.
    [Theory]
    [InlineData("untracked")]
    [InlineData("staged")]
    [InlineData("unstaged")]
    public async Task WhenADiffHasExactlyTheRowLimit_ShouldShowItWhole(string side)
    {
        using var fixture = await GitFixture.CreateAsync();
        var lines = Enumerable.Range(1, DiffLimits.LineLimit).Select(number => number + "\n").ToArray();
        if (side == "unstaged")
        {
            await fixture.WriteAsync("rows.txt", string.Concat(lines[..^1]));
            await fixture.CommitAsync("first", "rows.txt");
            await fixture.WriteAsync("rows.txt", string.Concat(lines[..^2]) + "changed\n");
        }
        else await fixture.WriteAsync("rows.txt", string.Concat(lines));
        if (side == "staged") await fixture.GitAsync("add", "rows.txt");
        var file = Assert.Single((await fixture.ReadAsync()).Files);

        var diff = await new CliFileDiffReader(fixture.Runner).ReadAsync(fixture.Root, file, side == "staged", false, fixture.Token);

        Assert.Equal((DiffContentKind.Text, DiffLimits.LineLimit), (diff.Kind, diff.Lines.Count(line => !line.IsHeader)));
    }

    private static async Task CreateDirectoryLinkAsync(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await output + await error);
    }
}
