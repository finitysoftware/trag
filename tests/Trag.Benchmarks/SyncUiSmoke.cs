using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Benchmarks;

internal static class SyncUiSmoke
{
    public static async Task RunAsync(string executable, string directory, CancellationToken ct)
    {
        var output = Directory.CreateDirectory(Path.GetFullPath(directory)).FullName;
        var fixture = Directory.CreateDirectory(Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N"))).FullName;
        var bare = Path.Combine(fixture, "origin.git"); var other = Path.Combine(fixture, "publish.git");
        var one = Path.Combine(fixture, "one"); var two = Path.Combine(fixture, "two");
        var runner = new GitProcessRunner();
        async Task<string> Git(string root, params string[] args)
        {
            var result = await runner.RunAsync(root, args, ct);
            if (result.ExitCode != 0) throw new InvalidOperationException("Fixture Git command failed: " + Encoding.UTF8.GetString(result.StandardError));
            return Encoding.UTF8.GetString(result.StandardOutput).Trim();
        }
        async Task Configure(string root)
        {
            await Git(root, "config", "user.name", "Sync UI Fixture"); await Git(root, "config", "user.email", "fixture@example.invalid");
            await Git(root, "config", "commit.gpgsign", "false"); await Git(root, "config", "core.autocrlf", "false");
            await Git(root, "config", "core.hooksPath", Path.Combine(fixture, "empty-hooks"));
        }
        async Task Commit(string root, string message)
        { await File.AppendAllTextAsync(Path.Combine(root, "application.txt"), message + "\n", ct); await Git(root, "add", "application.txt"); await Git(root, "commit", "-m", message); }
        await Git(fixture, "init", "--bare", "--initial-branch=trunk", bare);
        await Git(fixture, "-c", "core.autocrlf=false", "clone", bare, one); await Configure(one);
        await Commit(one, "Initial application"); await Git(one, "push", "-u", "origin", "trunk");
        await Git(fixture, "-c", "core.autocrlf=false", "clone", bare, two); await Configure(two);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var token = timeout.Token; var transcript = new List<string>(); var input = new List<double>();
        await using var ui = new ProbeSession(executable, Path.Combine(output, "settings.json"), transcript, token);
        Task Shot(string name) => ui.Shot(Path.Combine(output, name + ".png"), 250);
        static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        static string Quote(string path) => "'" + path.Replace('\\', '/').Replace("'", "'\\''", StringComparison.Ordinal) + "'";
        try
        {
            await ui.Expect("theme light", "TRAG_THEME_OK"); await ui.Expect("open " + one, "TRAG_OPEN 1 0 0 0");
            await ui.Expect("sync-state", "TRAG_SYNC 0 0 1 1 1 origin refs/heads/trunk");
            await ui.Expect("sync-counts", "TRAG_SYNC_COUNTS ↑ 0 ahead  ·  ↓ 0 behind");
            await Commit(two, "Update from the other clone"); await Git(two, "push", "origin", "trunk");
            await ui.Expect("sync-fetch", "TRAG_SYNC_RESULT Fetch completed. Local branch references are up to date for this fetch.");
            await ui.Expect("sync-counts", "TRAG_SYNC_COUNTS ↑ 0 ahead  ·  ↓ 1 behind"); await Shot("fetched-light");
            await ui.Expect("sync-pull", "TRAG_SYNC_RESULT Pull completed as a fast-forward. No merge commit was created.");
            if (await Git(one, "rev-parse", "HEAD") != await Git(two, "rev-parse", "HEAD")) throw new InvalidOperationException("Fast-forward target mismatch.");
            await Commit(one, "Local commit for publishing"); await ui.Expect("refresh", "TRAG_REFRESH 1 0 0 0"); await ui.Command("sync-state", "TRAG_SYNC");
            await ui.Expect("sync-counts", "TRAG_SYNC_COUNTS ↑ 1 ahead  ·  ↓ 0 behind");
            await ui.Expect("sync-push", "TRAG_SYNC_RESULT Push completed and the destination commit was verified.");
            if (await Git(one, "rev-parse", "HEAD") != await Git(bare, "rev-parse", "HEAD")) throw new InvalidOperationException("Push destination mismatch.");
            await ui.Expect("sync-counts", "TRAG_SYNC_COUNTS ↑ 0 ahead  ·  ↓ 0 behind");
            Console.WriteLine("Native fetch, fast-forward and push exchange passed.");
            var script = Path.Combine(fixture, "delayed-upload.sh"); var marker = Path.Combine(fixture, "transfer-started");
            await File.WriteAllTextAsync(script, "#!/bin/sh\necho ready > " + Quote(marker) + "\nsleep 1\nexec git-upload-pack \"$@\"\n", new UTF8Encoding(false), token);
            await Git(one, "config", "remote.origin.uploadpack", "sh " + Quote(script));
            // The remote's Git configuration changed underneath the open repository, and none of it is in the status.
            // The refresh has to read it again; without that the fetch below is refused for a configuration Trag never read.
            await ui.Expect("refresh", "TRAG_REFRESH 1 0 0 0"); await ui.Command("sync-state", "TRAG_SYNC");
            await ui.Expect("sync-start fetch", "TRAG_SYNC_STARTED"); await ui.Command("history", "TRAG_HISTORY");
            while (true)
            {
                input.Add(await ui.Input(TimeSpan.FromSeconds(5)));
                if (await ui.Command("sync-poll", "TRAG_SYNC") == "TRAG_SYNC 0 0 1 1 1 origin refs/heads/trunk") break;
                await Task.Delay(20, token);
            }
            await ui.Expect("sync-result", "TRAG_SYNC_RESULT Fetch completed. Local branch references are up to date for this fetch.");
            await ui.Expect("theme dark", "TRAG_THEME_OK"); await Shot("history-sync-dark");
            var cancelMarker = Path.Combine(fixture, "cancel-started");
            await File.WriteAllTextAsync(script, "#!/bin/sh\necho ready > " + Quote(cancelMarker) + "\nsleep 30\nexec git-upload-pack \"$@\"\n", new UTF8Encoding(false), token);
            await ui.Expect("sync-start fetch", "TRAG_SYNC_STARTED");
            await ProbeSession.WaitForFile(cancelMarker, token);
            await ui.Expect("cancel", "TRAG_CANCEL_OK");
            while (await ui.Command("sync-poll", "TRAG_SYNC") != "TRAG_SYNC 0 0 1 1 1 origin refs/heads/trunk") await Task.Delay(20, token);
            var cancelled = await ui.Command("sync-result", "TRAG_SYNC_RESULT");
            if (!cancelled.Contains("canceled", StringComparison.Ordinal)) throw new InvalidOperationException(cancelled);
            // An explicit refresh reads the remote configuration again too: this fetch follows a configuration change
            // and nothing else, so a refusal here would mean the panel kept what Git no longer says.
            await Git(one, "config", "--unset-all", "remote.origin.uploadpack");
            await ui.Expect("refresh", "TRAG_REFRESH 1 0 0 0"); await ui.Command("sync-state", "TRAG_SYNC");
            await ui.Expect("sync-fetch", "TRAG_SYNC_RESULT Fetch completed. Local branch references are up to date for this fetch.");
            await Git(one, "branch", "--unset-upstream");
            await ui.Expect("close", "TRAG_CLOSE 0 0 0 0"); await ui.Expect("open " + one, "TRAG_OPEN 1 0 0 0");
            await ui.Expect("sync-state", "TRAG_SYNC 0 0 0 0 0 - -"); await ui.Expect("sync-counts", "TRAG_SYNC_COUNTS Ahead / behind unknown");
            await ui.Expect("sync-remote " + Encode("origin"), "TRAG_SYNC_SELECTION_OK"); await ui.Expect("sync-state", "TRAG_SYNC 0 0 1 0 0 origin -");
            await ui.Expect("sync-branch " + Encode("refs/heads/trunk"), "TRAG_SYNC_SELECTION_OK");
            await Git(fixture, "clone", "--bare", bare, other); await Git(one, "remote", "set-url", "--push", "origin", other);
            await Commit(one, "Publish to separate endpoint"); await ui.Expect("refresh", "TRAG_REFRESH 1 0 0 0"); await ui.Command("sync-state", "TRAG_SYNC");
            var targetLine = await ui.Command("sync-target", "TRAG_SYNC_TARGET");
            var target = Encoding.UTF8.GetString(Convert.FromBase64String(targetLine[17..]));
            if (!target.Contains("publish.git", StringComparison.Ordinal)) throw new InvalidOperationException("Separate push address was not displayed.");
            var before = await Git(bare, "rev-parse", "HEAD");
            await ui.Expect("sync-push", "TRAG_SYNC_RESULT Push completed and the destination commit was verified. Ahead and behind still compare with "
                + await Git(one, "remote", "get-url", "origin") + " and change after a fetch from there.");
            if (await Git(bare, "rev-parse", "HEAD") != before || await Git(other, "rev-parse", "HEAD") != await Git(one, "rev-parse", "HEAD")) throw new InvalidOperationException("Push went to the wrong endpoint.");
            await ui.Expect("size 880 580", "TRAG_SIZE_OK");
            await Task.Delay(1500, token); // Allow the post-write watcher reconciliation to settle.
            await ui.Expect("sync-state", "TRAG_SYNC 0 0 1 1 1 origin refs/heads/trunk");
            await Shot("explicit-target-compact-dark");
            await ui.ExpectCompactScreenshot(Path.Combine(output, "explicit-target-compact-dark.png"));
            await ui.Expect("close", "TRAG_CLOSE 0 0 0 0");
            Console.WriteLine("Waiting 30 seconds for memory after releasing the sync session…");
            await Task.Delay(TimeSpan.FromSeconds(30), token); var resident = ui.Resident();
            if (await ui.QuitAsync() != 0) throw new InvalidOperationException("UI process failed.");
            var p95 = input.Order().ElementAt((int)Math.Ceiling(input.Count * .95) - 1);
            await File.WriteAllTextAsync(Path.Combine(output, "sync-smoke.json"), JsonSerializer.Serialize(new
            {
                TimestampUtc = DateTimeOffset.UtcNow, ExecutableSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executable, token))),
                Fixture = fixture, NativeInputMs = input, NativeInputP95Ms = p95, NativeInputMaxMs = input.Max(),
                ResidentBytesAfterSyncReleasedAnd30Seconds = resident, MemoryBudgetBytes = 100_000_000, MemoryBudgetPassed = resident <= 100_000_000,
                PreferredMemoryTargetPassed = resident < 60_000_000, Transcript = transcript, StandardError = await ui.Errors, FunctionalResult = "passed"
            }, new JsonSerializerOptions { WriteIndented = true }), token);
            if (p95 >= 100) throw new InvalidOperationException("Native input p95 exceeded 100 ms during sync.");
            Console.WriteLine($"Synchronization UI passed; input p95 {p95:F3} ms; released memory {resident / 1_000_000.0:F1} MB.");
        }
        catch (Exception error)
        {
            await File.WriteAllLinesAsync(Path.Combine(output, "failed-transcript.txt"), transcript, CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(output, "failure.txt"), error + "\nExit code: "
                + (ui.Process.HasExited ? ui.Process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) : "still running"), CancellationToken.None);
            throw;
        }
    }
}
