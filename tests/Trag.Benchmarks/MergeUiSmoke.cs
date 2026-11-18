using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Benchmarks;

internal static class MergeUiSmoke
{
    public static async Task RunAsync(string executable, string directory, CancellationToken ct)
    {
        var output = Directory.CreateDirectory(Path.GetFullPath(directory)).FullName;
        var fixture = Directory.CreateDirectory(Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N"))).FullName;
        var bare = Path.Combine(fixture, "origin.git"); var one = Path.Combine(fixture, "one"); var two = Path.Combine(fixture, "two");
        var runner = new GitProcessRunner(); var transcript = new List<string>(); var input = new List<double>();
        var settings = Path.Combine(output, "settings.json");
        async Task<string> Git(string root, params string[] args)
        {
            var result = await runner.RunAsync(root, args, ct);
            if (result.ExitCode != 0) throw new InvalidOperationException(Encoding.UTF8.GetString(result.StandardError));
            return Encoding.UTF8.GetString(result.StandardOutput).Trim();
        }
        async Task Configure(string root)
        {
            await Git(root, "config", "user.name", "Merge UI Fixture"); await Git(root, "config", "user.email", "fixture@example.invalid");
            await Git(root, "config", "commit.gpgsign", "false"); await Git(root, "config", "core.autocrlf", "false");
            await Git(root, "config", "core.hooksPath", Path.Combine(fixture, "empty-hooks"));
        }
        async Task Commit(string root, string message)
        { await File.AppendAllTextAsync(Path.Combine(root, "application.txt"), message + "\n", ct); await Git(root, "add", "application.txt"); await Git(root, "commit", "-m", message); }
        static string Quote(string path) => "'" + path.Replace('\\', '/').Replace("'", "'\\''", StringComparison.Ordinal) + "'";
        static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        // A compact picture is taken at the minimum size and checked against the window's own bounds.
        async Task Shot(ProbeSession ui, string name)
        {
            if (name.Contains("compact", StringComparison.Ordinal)) await ui.Expect("size 880 580", "TRAG_SIZE_OK");
            var path = Path.Combine(output, name + ".png"); await ui.Shot(path, 300);
            if (name.Contains("compact", StringComparison.Ordinal)) await ui.ExpectCompactScreenshot(path);
        }
        async Task WaitIdle(ProbeSession ui)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(40));
            while ((await ui.Command("merge-state", "TRAG_MERGE")).Split(' ')[4] == "1") await Task.Delay(100, timeout.Token);
        }
        await Git(fixture, "init", "--bare", "--initial-branch=trunk", bare);
        await Git(fixture, "-c", "core.autocrlf=false", "clone", bare, one); await Configure(one);
        await Commit(one, "Initial application"); await Git(one, "push", "-u", "origin", "trunk");
        await Git(fixture, "-c", "core.autocrlf=false", "clone", bare, two); await Configure(two);
        var script = Path.Combine(fixture, "review conflict.sh");
        await File.WriteAllTextAsync(script, "#!/bin/sh\nsleep 2\nprintf 'Initial application\\nReviewed local and incoming edits\\n' > \"$1\"\n", new UTF8Encoding(false), ct);
        await Git(one, "config", "core.editor", "sh " + Quote(script));
        await Git(one, "config", "merge.guitool", "tragfixture");
        await Git(one, "config", "mergetool.tragfixture.cmd", "sh " + Quote(script) + " \"$MERGED\"");
        long resident;
        try
        {
            await using (var ui = new ProbeSession(executable, settings, transcript, ct, TimeSpan.FromSeconds(45)))
            {
                await ui.Expect("theme light", "TRAG_THEME_OK"); await ui.Expect("open " + one, "TRAG_OPEN 1 0 0 0");
                await File.AppendAllTextAsync(Path.Combine(one, "application.txt"), "Local UI edit\n", ct);
                await ui.Expect("refresh", "TRAG_REFRESH 1 0 1 0"); await ui.Expect("select unstaged 0", "TRAG_SELECTION_OK");
                await ui.Expect("stage", "TRAG_WRITE 1 1 0 0"); await ui.Expect("message " + Encode("Local commit from Trag"), "TRAG_MESSAGE_OK");
                await ui.Expect("commit", "TRAG_WRITE 1 0 0 0"); var local = await Git(one, "rev-parse", "HEAD");
                await Commit(two, "Incoming edit"); await Git(two, "push", "origin", "trunk"); var incoming = await Git(two, "rev-parse", "HEAD");
                await ui.Command("sync-state", "TRAG_SYNC");
                Check((await ui.Command("sync-pull", "TRAG_SYNC_RESULT")).Contains("merge in progress", StringComparison.Ordinal), "Pull did not enter conflict resolution.");
                await ui.Expect("merge-state", "TRAG_MERGE 1 1 0 0 0");
                await Shot(ui, "conflict-light"); await ui.Expect("theme dark", "TRAG_THEME_OK"); await ui.Expect("size 880 580", "TRAG_SIZE_OK");
                await ui.Expect("merge-layout", "TRAG_MERGE_LAYOUT 1"); await Shot(ui, "conflict-compact-dark");
                await ui.Expect("merge-start tool", "TRAG_MERGE_STARTED"); await ui.Command("history", "TRAG_HISTORY");
                for (var i = 0; i < 25; i++) input.Add(await ui.Input(TimeSpan.FromSeconds(5)));
                await WaitIdle(ui); await ui.Expect("merge-state", "TRAG_MERGE 1 1 0 0 0");
                Check((await Git(one, "ls-files", "-u")).Length != 0, "Merge tool staged the real index without review.");
                await ui.Expect("merge-view", "TRAG_MERGE_VIEW"); await ui.Command("merge-action stage", "TRAG_MERGE_RESULT");
                await ui.Expect("merge-state", "TRAG_MERGE 1 0 1 0 0");
                await ui.Expect("merge-message " + Encode("Reviewed merge from Trag\n\nBoth branches retained."), "TRAG_MERGE_MESSAGE_OK");
                await Shot(ui, "resolved-compact-dark"); await ui.Command("merge-action finish", "TRAG_MERGE_RESULT");
                Check(await Git(one, "show", "-s", "--format=%P", "HEAD") == local + " " + incoming, "Merge parent verification failed.");
                await ui.Command("sync-state", "TRAG_SYNC"); await ui.Command("sync-push", "TRAG_SYNC_RESULT");
                Check(await Git(one, "rev-parse", "HEAD") == await Git(bare, "rev-parse", "HEAD"), "Merge push did not reach the selected remote.");
                await ui.Expect("close", "TRAG_CLOSE 0 0 0 0");
            }
            Console.WriteLine("Native commit, conflicted pull, external tool, stage, finish and push passed.");
            await Git(two, "pull", "--ff-only");
            await Git(one, "rm", "application.txt"); await Git(one, "commit", "-m", "Local deletion");
            await Commit(two, "Incoming edit after merge"); await Git(two, "push", "origin", "trunk"); await Git(one, "fetch");
            var terminalMerge = await runner.RunAsync(one, ["merge", "--no-edit", "origin/trunk"], ct); Check(terminalMerge.ExitCode == 1, "Terminal delete conflict missing.");
            await File.WriteAllTextAsync(Path.Combine(one, "keep.txt"), "Keep this untracked file\n", ct);
            var original = await Git(one, "rev-parse", "HEAD");
            await using (var ui = new ProbeSession(executable, settings, transcript, ct, TimeSpan.FromSeconds(45)))
            {
                await ui.Command("open " + one, "TRAG_OPEN"); await ui.Expect("merge-state", "TRAG_MERGE 1 1 0 0 0");
                await ui.Expect("theme light", "TRAG_THEME_OK"); await ui.Expect("size 880 580", "TRAG_SIZE_OK");
                await ui.Expect("merge-action abort", "TRAG_MERGE_ACTION_OK"); await ui.Expect("merge-state", "TRAG_MERGE 1 1 0 0 1"); await Shot(ui, "abort-confirmation-compact-light");
                await ui.Expect("merge-action keep", "TRAG_MERGE_ACTION_OK"); Check(File.Exists(Path.Combine(one, ".git", "MERGE_HEAD")), "Keep merge aborted the merge.");
                var marker = Path.Combine(fixture, "editor-started");
                await File.WriteAllTextAsync(script, "#!/bin/sh\necho ready > " + Quote(marker) + "\nsleep 30\n", new UTF8Encoding(false), ct);
                await ui.Expect("merge-start editor", "TRAG_MERGE_STARTED");
                await ProbeSession.WaitForFile(marker, ct);
                await ui.Command("history", "TRAG_HISTORY"); await ui.Expect("cancel", "TRAG_CANCEL_OK"); await WaitIdle(ui);
                await ui.Expect("merge-view", "TRAG_MERGE_VIEW"); await ui.Expect("merge-state", "TRAG_MERGE 1 1 0 0 0");
                await ui.Expect("merge-action abort", "TRAG_MERGE_ACTION_OK"); await ui.Command("merge-action confirm-abort", "TRAG_MERGE_RESULT");
                await ui.Expect("merge-state", "TRAG_MERGE 0 0 0 0 0");
                Check(await Git(one, "rev-parse", "HEAD") == original && !File.Exists(Path.Combine(one, ".git", "MERGE_HEAD")), "Abort did not restore the pre-merge state.");
                Check(File.Exists(Path.Combine(one, "keep.txt")), "Abort deleted unrelated untracked content.");
                await ui.Expect("close", "TRAG_CLOSE 0 0 0 0");
                Console.WriteLine("Restart, terminal delete conflict, editor cancellation and explicit abort passed; waiting 30 seconds for released memory.");
                await Task.Delay(TimeSpan.FromSeconds(30), ct); resident = ui.Resident();
            }
            var p95 = input.Order().ElementAt((int)Math.Ceiling(input.Count * .95) - 1);
            await File.WriteAllTextAsync(Path.Combine(output, "merge-smoke.json"), JsonSerializer.Serialize(new
            {
                TimestampUtc = DateTimeOffset.UtcNow, ExecutableSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executable, ct))),
                Fixture = fixture, NativeInputMs = input, NativeInputP95Ms = p95, NativeInputMaxMs = input.Max(),
                ResidentBytesAfterMergeReleasedAnd30Seconds = resident, MemoryBudgetBytes = 100_000_000, MemoryBudgetPassed = resident <= 100_000_000,
                Transcript = transcript, FunctionalResult = "passed"
            }, new JsonSerializerOptions { WriteIndented = true }), ct);
            Check(p95 < 100, "Merge input latency budget failed.");
            Console.WriteLine($"Merge UI passed; native input p95 {p95:F3} ms; released memory {resident / 1_000_000.0:F1} MB.");
        }
        catch (Exception error)
        {
            await File.WriteAllLinesAsync(Path.Combine(output, "failed-transcript.txt"), transcript, CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(output, "failure.txt"), error.ToString(), CancellationToken.None); throw;
        }
    }
}
