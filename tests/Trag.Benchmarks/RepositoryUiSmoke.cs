using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Platform.Storage;

namespace Trag.Benchmarks;

internal static class RepositoryUiSmoke
{
    public static async Task RunAsync(string executable, string outputDirectory, CancellationToken ct, bool live = false)
    {
        var output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        var fixture = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var runner = new GitProcessRunner();
        async Task Git(params string[] args)
        {
            var result = await runner.RunAsync(fixture, args, ct);
            if (result.ExitCode != 0) throw new InvalidOperationException("Fixture setup failed: " + Encoding.UTF8.GetString(result.StandardError));
        }
        await Git("init", "--initial-branch=feature/status");
        await Git("config", "user.name", "Trag Fixture");
        await Git("config", "user.email", "fixture@example.invalid");
        await Git("config", "commit.gpgsign", "false");
        await Git("config", "core.hooksPath", Path.Combine(fixture, ".git", "empty-hooks"));
        var partial = Path.Combine(fixture, "application.cs");
        await File.WriteAllTextAsync(partial, "// initial content\n", ct);
        await File.WriteAllTextAsync(Path.Combine(fixture, "old name.txt"), "rename fixture\n", ct);
        await Git("add", "--", "application.cs", "old name.txt");
        await Git("commit", "-m", "Initial fixture");
        await File.AppendAllTextAsync(partial, "// staged change\n", ct);
        await Git("add", "--", "application.cs");
        await File.AppendAllTextAsync(partial, "// unstaged change\n", ct);
        await Git("mv", "--", "old name.txt", "new name č.txt");
        await File.WriteAllTextAsync(Path.Combine(fixture, "notes.md"), "untracked file\n", ct);
        await Git("config", "remote.origin.url", "https://example.invalid/finity/trag-fixture.git");
        await Git("config", "remote.origin.pushurl", "ssh://git@example.invalid/finity/trag-fixture.git");
        var indexFile = Path.Combine(fixture, ".git", "index");
        var originalIndex = SHA256.HashData(await File.ReadAllBytesAsync(indexFile, ct));
        var settings = new LastRepositoryStore(Path.Combine(output, "fixture-settings.json"));
        await settings.SaveAsync(fixture, ct);
        if (await settings.LoadAsync(ct) != fixture) throw new InvalidOperationException("Settings roundtrip failed.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var token = timeout.Token;
        var transcript = new List<string>();
        var inputSamples = new List<double>();
        await using var ui = new ProbeSession(executable, Path.Combine(output, "fixture-settings.json"), transcript, token);
        Task Screenshot(string name) => ui.Shot(Path.Combine(output, name + ".png"), 250);
        Task Until(string command, string expected) => ui.Until(command, expected, TimeSpan.FromSeconds(30), 100);
        await ui.Expect("theme light", "TRAG_THEME_OK");
        await Screenshot("empty-light");
        await ui.Expect("restore", "TRAG_RESTORE 1 2 2 0");
        var timer = Stopwatch.StartNew();
        await ui.Expect("open " + fixture, "TRAG_OPEN 1 2 2 0");
        var openMilliseconds = timer.Elapsed.TotalMilliseconds;
        await ui.Expect("select staged 0", "TRAG_SELECTION_OK");
        if (live) await Until("diff-contains +// staged change", "TRAG_DIFF_CONTAINS 1");
        await Screenshot("status-light");
        if (live)
        {
            await ui.Expect("select unstaged 0", "TRAG_SELECTION_OK");
            await Until("diff-contains +// unstaged change", "TRAG_DIFF_CONTAINS 1");
            await File.AppendAllTextAsync(partial, "// live editor change\n", token);
            await Until("diff-contains +// live editor change", "TRAG_DIFF_CONTAINS 1");
        }
        await ui.Expect("theme dark", "TRAG_THEME_OK");
        await Screenshot("status-dark");
        await ui.Expect("size 880 580", "TRAG_SIZE_OK");
        await Screenshot("status-compact");
        await File.WriteAllTextAsync(Path.Combine(fixture, "added-externally.txt"), "refresh fixture\n", token);
        if (live)
        {
            await Until("status", "TRAG_STATUS 1 2 3 0");
            await Until("diff-contains +// live editor change", "TRAG_DIFF_CONTAINS 1");
            var edits = Task.Run(async () =>
            {
                for (var i = 0; i < 10_000; i++) await File.WriteAllTextAsync(Path.Combine(fixture, $"z-mass-{i:D5}.txt"), "mass update\n", token);
            }, token);
            var massTimer = Stopwatch.StartNew();
            while (massTimer.Elapsed < TimeSpan.FromSeconds(30))
            {
                inputSamples.Add(await ui.Input());
                if (await ui.Command("status", "TRAG_STATUS") == "TRAG_STATUS 1 2 10003 0") break;
                await Task.Delay(20, token);
            }
            await edits;
            await Until("status", "TRAG_STATUS 1 2 10003 0");
            await Until("diff-contains +// live editor change", "TRAG_DIFF_CONTAINS 1");
            await Screenshot("mass-changes-dark");
            // These exact files belong to this newly created fixture; never touch the source repository.
            for (var i = 0; i < 10_000; i++) File.Delete(Path.Combine(fixture, $"z-mass-{i:D5}.txt"));
            await Until("status", "TRAG_STATUS 1 2 3 0");
            await ui.Expect("select unstaged 0", "TRAG_SELECTION_OK");
            await Until("diff-contains +refresh fixture", "TRAG_DIFF_CONTAINS 1");
            await Screenshot("untracked-dark");
        }
        else await ui.Expect("refresh", "TRAG_REFRESH 1 2 3 0");
        await ui.Expect("close", "TRAG_CLOSE 0 0 0 0");
        await ui.Expect("open " + Path.Combine(output, "missing-" + Guid.NewGuid().ToString("N")), "TRAG_OPEN 0 0 0 1");
        await Screenshot("error-dark");
        await ui.Expect("open " + fixture, "TRAG_OPEN 1 2 3 0");
        await ui.Expect("close", "TRAG_CLOSE 0 0 0 0");
        if (await ui.QuitAsync() != 0) throw new InvalidOperationException("UI smoke exited unsuccessfully.");
        var finalIndex = SHA256.HashData(await File.ReadAllBytesAsync(indexFile, token));
        if (!originalIndex.AsSpan().SequenceEqual(finalIndex))
            throw new InvalidOperationException("Repository reads changed the index.");
        await File.WriteAllTextAsync(Path.Combine(output, "ui-smoke.json"), JsonSerializer.Serialize(new
        {
            TimestampUtc = DateTimeOffset.UtcNow, Executable = Path.GetFullPath(executable),
            ExecutableSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executable, token))),
            Fixture = fixture, OpenMilliseconds = openMilliseconds, Transcript = transcript,
            StandardError = await ui.Errors, IndexUnchanged = true, LiveUpdates = live,
            NativeInputDuringMassChangesMs = inputSamples,
            NativeInputP95Ms = inputSamples.Count == 0 ? (double?)null : inputSamples.Order().ElementAt((int)Math.Ceiling(inputSamples.Count * 0.95) - 1),
            NativeInputMaxMs = inputSamples.Count == 0 ? (double?)null : inputSamples.Max(), Result = "passed"
        }, new JsonSerializerOptions { WriteIndented = true }), token);
        Console.WriteLine($"Production open/refresh/close UI smoke passed; open {openMilliseconds:F1} ms; index unchanged.");
        if (inputSamples.Count != 0 && inputSamples.Order().ElementAt((int)Math.Ceiling(inputSamples.Count * 0.95) - 1) >= 100)
            throw new InvalidOperationException("Native input p95 exceeded 100 ms during mass changes.");
    }
}
