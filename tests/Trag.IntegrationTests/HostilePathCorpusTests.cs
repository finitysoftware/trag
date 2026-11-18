using System.Text;
using Trag.Domain.Paths;
using Trag.TestSupport;

namespace Trag.IntegrationTests;

// The corpus keeps its promise on the machine it runs on: each path it says this machine holds is a file of exactly that
// name that Git tracks under exactly those bytes, each name it says Git for Windows refuses stays on disk and out of the
// index, and the case twins are two files only where the file system tells case apart. A theory takes a label, so a
// newline or a 4,000-byte path never reaches a test's name.
public sealed class HostilePathCorpusTests
{
    public static TheoryData<string> TrackedHere => new(TestNames.Hostile.Where(path => path.Here == TestNames.PathFate.Tracked).Select(path => path.Label));
    // The names the corpus says Git for Windows refuses, the same on every system, so the data is never empty.
    public static TheoryData<string> RefusedOnWindows => new(TestNames.Hostile.Where(path => path.OnWindows == TestNames.PathFate.RefusedByGit).Select(path => path.Label));

    // The names the file system holds in the folder of a corpus path, spelled as it stores them, and the path's own name.
    private static (string[] Names, string Leaf) OnDisk(GitFixture f, string path)
    {
        var slash = path.LastIndexOf('/');
        var folder = Path.Combine(f.Root, slash < 0 ? "" : path[..slash]);
        return ([.. Directory.EnumerateFileSystemEntries(folder).Select(entry => Path.GetFileName(entry))], path[(slash + 1)..]);
    }

    [Theory]
    [MemberData(nameof(TrackedHere))]
    public async Task WhenACorpusPathThisMachineHoldsIsCommitted_ShouldBeTrackedAndOnDiskUnderExactlyItsName(string label)
    {
        var path = TestNames.Find(label).Path;
        using var f = await GitFixture.CreateAsync();

        await f.WriteAsync(path, "corpus\n");
        await f.CommitAsync("Corpus", path);

        var (names, leaf) = OnDisk(f, path);
        Assert.Contains(leaf, names);
        Assert.Equal(Encoding.UTF8.GetBytes(path + "\0"), (await f.RunAsync("ls-files", "-z")).StandardOutput);
    }

    // Windows 11 writes a reserved device name with an extension as an ordinary file, and Git for Windows refuses to
    // index it.
    [Theory]
    [MemberData(nameof(RefusedOnWindows))]
    public async Task WhenANameGitForWindowsRefusesIsWritten_ShouldStayOnDiskAndOutOfTheIndex(string label)
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = TestNames.Find(label).Path;
        using var f = await GitFixture.CreateAsync();
        await f.WriteAsync(path, "corpus\n");

        var add = await f.RunAsync("add", "--", path);

        Assert.NotEqual(0, add.ExitCode);
        Assert.Contains(path, OnDisk(f, path).Names);
        Assert.Empty((await f.RunAsync("ls-files", "-z")).StandardOutput);
    }

    // Git keeps both entries where the file system ignores case, and a checkout there writes them to one file. Each twin
    // is committed straight into the index, since there no file can hold both.
    [Fact]
    public async Task WhenTwoCommittedNamesDifferOnlyInCase_ShouldCheckOutBothOnlyWhereTheFileSystemTellsThemApart()
    {
        var (first, second) = TestNames.CaseTwins;
        using var f = await GitFixture.CreateAsync();
        foreach (var name in new[] { first, second })
        {
            var blob = await f.Runner.RunAsync(f.Root, ["hash-object", "-w", "--stdin"], f.Token, standardInput: Encoding.UTF8.GetBytes(name + "\n"));
            await f.GitAsync("update-index", "--add", "--cacheinfo", "100644," + Encoding.UTF8.GetString(blob.StandardOutput).Trim() + "," + name);
        }
        await f.GitAsync("commit", "-m", "Twins");

        await f.GitAsync("reset", "--hard");

        var tracked = (await f.GitAsync("ls-files", "-z")).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(new[] { first, second }.Order(StringComparer.Ordinal), tracked);
        var twinsOnDisk = Directory.EnumerateFiles(f.Root).Count(file => string.Equals(Path.GetFileName(file), first, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(LocalPaths.Comparison == StringComparison.Ordinal ? 2 : 1, twinsOnDisk);
    }
}
