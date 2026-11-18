using Trag.Application.Workspace;
using Trag.Domain.Status;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Platform.Watching;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// The live lists never fall behind: after any mix of changes another program makes, in any rhythm, the watched lists
// settle on exactly what a full read of Git's status gives. They are compared after every fifth change, so a change no
// later one happens to cover is caught too. Settling may take a read the repository moved under, which is tried again
// after two seconds, or a poll five seconds apart, so the lists have ten seconds to agree.
public sealed class LiveRefreshPropertyTests
{
    [Theory]
    [InlineData(3412)]
    [InlineData(21)]
    public async Task WhenOutsideChangesFollowEachOtherAtRandom_ShouldSettleOnGitsStatus(int seed)
    {
        using var fixture = await GitFixture.CreateAsync(scenarioTimeout: TimeSpan.FromMinutes(8));
        // Under autocrlf Git lists a file rewritten with LF as changed although it diffs as unchanged.
        await fixture.GitAsync("config", "core.autocrlf", "false");
        await fixture.WriteAsync("seed.txt", "seed\n");
        await fixture.CommitAsync("Seed", "seed.txt");
        await using var workspace = new RepositoryWorkspace(new CliRepositoryReader(fixture.Runner), new RepositoryWatcher());
        await workspace.OpenAsync(fixture.Root, fixture.Token);
        var latest = new Latest();
        var watching = latest.FollowAsync(workspace, fixture.Token);
        var random = new Random(seed);
        var log = new List<string>();

        for (var step = 1; step <= 200; step++)
        {
            log.Add(await ChangeAsync(fixture, random));
            await Task.Delay(random.Next(0, 501), fixture.Token);
            if (step % 5 != 0) continue;
            var git = Rows(await fixture.ReadAsync()).ToArray();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!git.SequenceEqual(latest.Rows()) && DateTime.UtcNow < deadline) await Task.Delay(100, fixture.Token);
            Assert.True(git.SequenceEqual(latest.Rows()), "Seed " + seed + ", step " + step + ": watched " + string.Join(", ", latest.Rows())
                + " but Git lists " + string.Join(", ", git) + " after " + string.Join("; ", log));
        }
        await workspace.CloseAsync();
        await watching;
    }

    // The root holds half the names: a file there changes no folder the watcher reports, so it is read by its path alone.
    private static readonly string[] Folders = ["", "", "", "d0/", "d1/", "d0/e/"];
    private static readonly string[] Names = ["a.txt", "b.txt", "c.txt", "A.txt"];

    // One change as another program makes it; answers what it did, for the failure message.
    private static async Task<string> ChangeAsync(GitFixture fixture, Random random)
    {
        var files = Directory.EnumerateFiles(fixture.Root, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(fixture.Root, file).Replace('\\', '/'))
            .Where(file => !file.StartsWith(".git/", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        string Any() => Folders[random.Next(Folders.Length)] + Names[random.Next(Names.Length)];
        string? Existing() => files.Length == 0 ? null : files[random.Next(files.Length)];
        string Full(string path) => Path.Combine(fixture.Root, path);
        // Writes are most of the changes, and two contents make a file flip between its committed content and a change.
        switch (random.Next(13))
        {
            case <= 5:
            {
                var path = Any();
                if (Directory.Exists(Full(path))) return "skip write " + path;
                await fixture.WriteAsync(path, "content " + random.Next(2) + "\n");
                return "write " + path;
            }
            case 6 when Existing() is { } path:
                File.Delete(Full(path));
                return "delete " + path;
            case 7 when Existing() is { } path:
            {
                var target = Any();
                if (Path.Exists(Full(target))) return "skip rename " + path + " " + target;
                Directory.CreateDirectory(Path.GetDirectoryName(Full(target))!);
                File.Move(Full(path), Full(target));
                return "rename " + path + " " + target;
            }
            case 8 when Existing() is { } path:
            {
                // A rename that changes only the case of the name, as Explorer makes one. Where the file system tells case
                // apart, that name may be another file already.
                var name = Path.GetFileName(path);
                var renamed = Path.Combine(Path.GetDirectoryName(Full(path))!, name == name.ToUpperInvariant() ? name.ToLowerInvariant() : name.ToUpperInvariant());
                if (files.Contains(Path.GetRelativePath(fixture.Root, renamed).Replace('\\', '/'), StringComparer.Ordinal)) return "skip case " + path;
                File.Move(Full(path), renamed);
                return "case " + path;
            }
            case 9:
                Directory.CreateDirectory(Full("d" + random.Next(2) + "/f" + random.Next(2)));
                return "mkdir";
            case 10:
                await fixture.GitAsync("add", "--all");
                return "add";
            case 11:
                await fixture.GitAsync("commit", "--quiet", "--allow-empty", "-m", "Step");
                return "commit";
            case 12 when Existing() is { } path:
                await fixture.RunAsync("checkout", "--", path);
                return "checkout " + path;
            default:
                return "nothing";
        }
    }

    private static IEnumerable<(string, string?, char, char)> Rows(RepositorySnapshot snapshot) =>
        snapshot.Files.Select(file => (file.Path.Display, file.OriginalPath?.Display, file.Index, file.Worktree)).Order();

    // The last snapshot the watch accepted.
    private sealed class Latest
    {
        private volatile RepositorySnapshot? snapshot;

        public async Task FollowAsync(RepositoryWorkspace workspace, CancellationToken ct)
        {
            await foreach (var update in workspace.WatchAsync(ct))
                if (update.Snapshot is { } accepted) snapshot = accepted;
        }

        public (string, string?, char, char)[] Rows() => snapshot is { } shown ? LiveRefreshPropertyTests.Rows(shown).ToArray() : [];
    }
}
