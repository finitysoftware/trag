using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Benchmarks;

internal static class HistoryUiSmoke
{
    public static async Task RunAsync(string executable, string directory, string largeRepository, CancellationToken ct, bool screenshots = true)
    {
        var output = Directory.CreateDirectory(Path.GetFullPath(directory)).FullName;
        var fixture = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N"));
        await FixtureGenerator.GenerateAsync(fixture, 207, 3, ct);
        var large = Path.GetFullPath(largeRepository);
        var runner = new GitProcessRunner();
        async Task<string> Git(string root, params string[] args)
        {
            var result = await runner.RunAsync(root, args, ct);
            if (result.ExitCode != 0) throw new InvalidOperationException(Encoding.UTF8.GetString(result.StandardError));
            return Encoding.UTF8.GetString(result.StandardOutput).Trim();
        }
        await Git(fixture, "config", "user.name", "History Fixture");
        await Git(fixture, "config", "user.email", "fixture@example.invalid");
        await Git(fixture, "config", "commit.gpgsign", "false");
        await Git(fixture, "config", "core.hooksPath", Path.Combine(fixture, ".git", "empty-hooks"));
        var initial = await Git(fixture, "rev-parse", "HEAD");
        var commitCount = int.Parse(await Git(large, "rev-list", "--count", "HEAD"), System.Globalization.CultureInfo.InvariantCulture);
        if (commitCount < 100_000) throw new InvalidOperationException("The large fixture must contain at least 100,000 commits.");
        var old = await Git(large, "rev-list", "--max-parents=0", "HEAD");
        var indexHash = SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(large, ".git", "index"), ct));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var token = timeout.Token;
        var transcript = new List<string>(); var input = new List<double>(); long peak = 0;
        var memory = new List<object>();
        await using var ui = new ProbeSession(executable, Path.Combine(output, "settings.json"), transcript, token);
        async Task Memory(string stage)
        {
            var info = (await ui.Command("memory", "TRAG_MEMORY")).Split(' ').Skip(1).Select(long.Parse).ToArray();
            var resident = ui.Resident();
            memory.Add(new { Stage = stage, ResidentBytes = resident, ManagedBytes = info[0], HeapBytes = info[1],
                CommittedBytes = info[2], FragmentedBytes = info[3], Gen0 = info[4], Gen1 = info[5], Gen2 = info[6] });
            Console.WriteLine($"{stage}: resident {resident / 1_000_000.0:F1} MB; managed {info[0] / 1_000_000.0:F1} MB.");
        }
        Task Until(string command, string expected) => ui.Until(command, expected, TimeSpan.FromSeconds(30), 50);
        async Task Screenshot(string name)
        { await Task.Delay(250, token); if (screenshots) await ui.Expect("screenshot " + Path.Combine(output, name + ".png"), "TRAG_SCREENSHOT_SAVED"); }
        static string Query(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        try
        {
            await ui.Expect("theme light", "TRAG_THEME_OK");
            await Memory("Empty");
            await ui.Expect("open " + fixture, "TRAG_OPEN 1 0 3 0");
            await ui.Expect("history", "TRAG_HISTORY 100 0 1 0");
            await ui.Expect("history-more", "TRAG_HISTORY 200 1 1 0");
            await ui.Expect("history-more", "TRAG_HISTORY 207 2 0 0");
            await ui.Expect("history-select 0", "TRAG_COMMIT " + initial + " 1");
            _ = await ui.Command("history-file 0", "TRAG_COMMIT_DIFF");
            await ui.Expect("history-contains +revision 206", "TRAG_HISTORY_CONTAINS 1");
            await Screenshot("history-light");
            await Git(fixture, "commit", "--allow-empty", "-m", "External new HEAD");
            await Until("history-state", "TRAG_HISTORY 100 0 1 0");
            var changedHead = await Git(fixture, "rev-parse", "HEAD");
            await ui.Expect("history-select 0", "TRAG_COMMIT " + changedHead + " 0");
            await Memory("Small history");
            await ui.Expect("open " + large, "TRAG_OPEN 1 0 10000 0");
            await Memory("Large status");
            await ui.Expect("history", "TRAG_HISTORY 100 0 1 0");
            for (var page = 1; page <= 5; page++) await ui.Expect("history-more", $"TRAG_HISTORY {(page + 1) * 100} {page} 1 0");
            var watch = Stopwatch.StartNew();
            await ui.Expect("search-start " + Query("Synthetic commit 000000"), "TRAG_SEARCH_STARTED");
            while (true)
            {
                input.Add(await ui.Input(TimeSpan.FromSeconds(5))); peak = Math.Max(peak, ui.Resident());
                if (await ui.Command("history-state", "TRAG_HISTORY") == "TRAG_HISTORY 1 0 0 0") break;
                if (watch.Elapsed > TimeSpan.FromSeconds(60)) throw new InvalidOperationException("Old commit search timed out.");
                await Task.Delay(20, token);
            }
            var searchMs = watch.Elapsed.TotalMilliseconds;
            await Memory("Old commit search");
            await ui.Expect("history-select 0", "TRAG_COMMIT " + old + " 10000");
            _ = await ui.Command("history-file 0", "TRAG_COMMIT_DIFF");
            await ui.Expect("history-contains +stable blame line", "TRAG_HISTORY_CONTAINS 1");
            await Memory("Root commit files and diff");
            await ui.Expect("theme dark", "TRAG_THEME_OK"); await Screenshot("old-root-search-dark");
            await ui.Expect("search " + Query(old[..12]), "TRAG_HISTORY 1 0 0 0");
            await ui.Expect("search " + Query("Fixture"), "TRAG_HISTORY 100 0 1 0");
            await ui.Expect("search " + Query("--all $(literal) [a-z]"), "TRAG_HISTORY 0 0 0 0");
            await ui.Expect("size 880 580", "TRAG_SIZE_OK"); await Screenshot("no-results-compact-dark");
            await ui.Expect("search-start " + Query("cancel this scan"), "TRAG_SEARCH_STARTED");
            await ui.Expect("cancel", "TRAG_CANCEL_OK"); await Until("history-state", "TRAG_HISTORY 0 0 0 0");
            await ui.Expect("close", "TRAG_CLOSE 0 0 0 0");
            await ui.Expect("history-state", "TRAG_HISTORY 0 0 0 0");
            await Memory("Released history");
            Console.WriteLine("Waiting 30 seconds for resident memory after releasing history…");
            await Task.Delay(TimeSpan.FromSeconds(30), token); var released = ui.Resident();
            await Memory("Released history after 30 seconds");
            var exit = await ui.QuitAsync();
            var finalIndexHash = SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(large, ".git", "index"), token));
            if (exit != 0 || !indexHash.AsSpan().SequenceEqual(finalIndexHash))
                throw new InvalidOperationException("History UI changed the fixture index or exited unsuccessfully.");
            var p95 = input.Order().ElementAt((int)Math.Ceiling(input.Count * 0.95) - 1);
            await File.WriteAllTextAsync(Path.Combine(output, "history-smoke.json"), JsonSerializer.Serialize(new
            {
                TimestampUtc = DateTimeOffset.UtcNow, ExecutableSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executable, token))),
                Fixture = fixture, LargeRepository = large, VerifiedCommitCount = commitCount, OldCommit = old, SearchMilliseconds = searchMs,
                NativeInputMs = input, NativeInputP95Ms = p95, NativeInputMaxMs = input.Max(), PeakSearchResidentBytes = peak,
                ResidentBytesAfterHistoryReleasedAnd30Seconds = released, MaximumCachedPages = 3, LargeIndexUnchanged = true,
                MemorySamples = memory, CapturedScreenshots = screenshots, Transcript = transcript, StandardError = await ui.Errors,
                FunctionalResult = "passed", InputBudgetPassed = p95 < 100, ReleasedMemoryBudgetBytes = 100_000_000,
                ReleasedMemoryBudgetPassed = released <= 100_000_000, PreferredMemoryTargetPassed = released < 60_000_000
            }, new JsonSerializerOptions { WriteIndented = true }), token);
            Console.WriteLine($"History UI passed: old commit search {searchMs:F1} ms; input p95 {p95:F3} ms; released memory {released / 1_000_000.0:F1} MB.");
            if (p95 >= 100) throw new InvalidOperationException("Input p95 exceeded 100 ms during history search.");
            // history-memory is the measurement run: the released-memory budget is a threshold there, not only a note in the JSON.
            if (!screenshots && released > 100_000_000) throw new InvalidOperationException("Released memory exceeded the agreed 100 MB limit.");
        }
        catch
        {
            await File.WriteAllLinesAsync(Path.Combine(output, "failed-transcript.txt"), transcript, CancellationToken.None);
            throw;
        }
    }
}
