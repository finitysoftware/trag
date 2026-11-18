using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Trag.Benchmarks;

internal static partial class UiBenchmark
{
    public static async Task MeasureAsync(string executable, string outputFile, int count, string? repository, CancellationToken ct)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        var samples = new List<double>();
        var dispatcherSamples = new List<double>();
        var sessionResidentSamples = new List<long>();
        long emptyBytes = 0, afterRepositoryBytes = 0;
        double? repositoryOpenMilliseconds = null;
        // Warmup is explicitly excluded; this command makes no cold-cache claim.
        for (var run = -1; run < count; run++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            var token = timeout.Token;
            var start = new ProcessStartInfo(Path.GetFullPath(executable))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new System.Text.UTF8Encoding(false)
            };
            start.ArgumentList.Add("--ui-probe");
            var watch = Stopwatch.StartNew();
            using var process = Process.Start(start) ?? throw new InvalidOperationException();
            using var cleanup = new ProcessCleanup(process);
            using var registration = token.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
            var stderr = process.StandardError.ReadToEndAsync(token);
            var acknowledgement = process.StandardOutput.ReadLineAsync(token).AsTask();
            while (!acknowledgement.IsCompleted)
            {
                if (process.HasExited) throw new InvalidOperationException("UI exited before accepting input.");
                if (OperatingSystem.IsWindows())
                {
                    process.Refresh();
                    if (process.MainWindowHandle != 0 && IsWindowVisible(process.MainWindowHandle) != 0)
                        PostMessage(process.MainWindowHandle, 0x100, 0x77, 0); // native F8 key-down
                }
                else
                {
                    var search = await Xdo(["search", "--onlyvisible", "--pid", process.Id.ToString()], token);
                    var window = search.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (window is not null) _ = await Xdo(["key", "--window", window, "F8"], token);
                }
                await Task.WhenAny(acknowledgement, Task.Delay(5, token));
                token.ThrowIfCancellationRequested();
            }
            if (await acknowledgement != "TRAG_INPUT_ACK") throw new InvalidOperationException("Missing native input acknowledgement.");
            if (run >= 0) samples.Add(watch.Elapsed.TotalMilliseconds);
            if (run == 0)
            {
                Console.WriteLine("Waiting 30 seconds for empty resident-memory sample…");
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                process.Refresh(); emptyBytes = process.WorkingSet64;
                if (repository is not null)
                {
                    // Drain any extra startup key acknowledgements before timing new input.
                    await process.StandardInput.WriteLineAsync("ping".AsMemory(), token);
                    while (await process.StandardOutput.ReadLineAsync(token) is { } pending && pending != "TRAG_DISPATCH_ACK") { }
                    var opening = Stopwatch.StartNew();
                    await process.StandardInput.WriteLineAsync(("open " + Path.GetFullPath(repository)).AsMemory(), token);
                    var released = false;
                    for (var ping = 0; ping < 100; ping++)
                    {
                        var latency = Stopwatch.StartNew();
                        if (OperatingSystem.IsWindows()) PostMessage(process.MainWindowHandle, 0x100, 0x77, 0);
                        else
                        {
                            var windows = await Xdo(["search", "--onlyvisible", "--pid", process.Id.ToString()], token);
                            _ = await Xdo(["key", "--window", windows.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0], "F8"], token);
                        }
                        while (true)
                        {
                            var line = await process.StandardOutput.ReadLineAsync(token);
                            if (line is null) throw new InvalidOperationException("UI probe ended unexpectedly.");
                            CheckRepositoryResponse(line);
                            if (RepositoryDone(line))
                            {
                                released = true;
                                repositoryOpenMilliseconds = opening.Elapsed.TotalMilliseconds;
                            }
                            if (line != "TRAG_INPUT_ACK") continue;
                            dispatcherSamples.Add(latency.Elapsed.TotalMilliseconds);
                            break;
                        }
                        if (released) break;
                        await Task.Delay(10, token);
                    }
                    while (!released)
                    {
                        var line = await process.StandardOutput.ReadLineAsync(token);
                        if (line is null) throw new InvalidOperationException("UI probe ended unexpectedly.");
                        CheckRepositoryResponse(line);
                        released = RepositoryDone(line);
                        if (released) repositoryOpenMilliseconds = opening.Elapsed.TotalMilliseconds;
                    }
                    for (var cycle = 0; cycle < 5; cycle++)
                    {
                        await process.StandardInput.WriteLineAsync("close".AsMemory(), token);
                        while (await process.StandardOutput.ReadLineAsync(token) is { } closed && closed != "TRAG_CLOSE 0 0 0 0") { }
                        process.Refresh(); sessionResidentSamples.Add(process.WorkingSet64);
                        if (cycle == 4) break;
                        await process.StandardInput.WriteLineAsync(("open " + Path.GetFullPath(repository)).AsMemory(), token);
                        while (await process.StandardOutput.ReadLineAsync(token) is { } reopened)
                        {
                            CheckRepositoryResponse(reopened);
                            if (RepositoryDone(reopened)) break;
                        }
                    }
                    await Task.Delay(TimeSpan.FromSeconds(30), token);
                    process.Refresh(); afterRepositoryBytes = process.WorkingSet64;
                }
                // Export after memory sampling so bitmap allocation does not affect it.
                var screenshot = Path.ChangeExtension(Path.GetFullPath(outputFile), ".png");
                if (repository is not null)
                {
                    await process.StandardInput.WriteLineAsync(("open " + Path.GetFullPath(repository)).AsMemory(), token);
                    while (await process.StandardOutput.ReadLineAsync(token) is { } opened)
                    {
                        CheckRepositoryResponse(opened);
                        if (RepositoryDone(opened)) break;
                    }
                }
                else await process.StandardInput.WriteLineAsync("preview".AsMemory(), token);
                await Task.Delay(500, token);
                await process.StandardInput.WriteLineAsync(("screenshot " + screenshot).AsMemory(), token);
                while (await process.StandardOutput.ReadLineAsync(token) is { } line && line != "TRAG_SCREENSHOT_SAVED") { }
            }
            await process.StandardInput.WriteLineAsync("quit".AsMemory(), token);
            await process.WaitForExitAsync(token);
            _ = await stderr;
            if (process.ExitCode != 0) throw new InvalidOperationException("UI probe failed.");
            Console.WriteLine($"Run {run + 1}/{count}: {(run >= 0 ? samples[^1] : watch.Elapsed.TotalMilliseconds):F1} ms");
        }
        var report = new
        {
            TimestampUtc = DateTimeOffset.UtcNow, OS = RuntimeInformation.OSDescription,
            ExecutableSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(executable, ct))),
            Architecture = RuntimeInformation.OSArchitecture.ToString(), LogicalProcessors = Environment.ProcessorCount,
            Cache = "Warm series after one excluded warmup; cold cache not tested",
            RepositoryMode = repository is null ? "Empty session, no repository opened" : "Production session, status lists and five open/close cycles",
            ResidentBytesImmediatelyAfterEachClose = sessionResidentSamples,
            Method = "Process start to visible native window F8 handler acknowledgement; presentation must be visually cross-checked",
            StartupSamplesMs = samples, StartupP95Ms = Percentile(samples),
            RepositoryOpenMilliseconds = repositoryOpenMilliseconds,
            EmptyResidentBytesAfter30Seconds = emptyBytes, ResidentBytesAfterRepositoryReleasedAnd30Seconds = afterRepositoryBytes,
            NativeInputDuringGitSamplesMs = dispatcherSamples,
            NativeInputDuringGitP95Ms = dispatcherSamples.Count == 0 ? (double?)null : Percentile(dispatcherSamples),
            NativeInputDuringGitMaxMs = dispatcherSamples.Count == 0 ? (double?)null : dispatcherSamples.Max(),
            ChildProcessAccounting = "Git calls are awaited and disposed within the session",
            Acceptance = "Incomplete: cold cache, clean machine, frame presentation and full input traces still required"
        };
        await File.WriteAllTextAsync(outputFile, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), ct);
        Console.WriteLine($"p95 {Percentile(samples):F1} ms; empty {emptyBytes / 1_000_000d:F1} MB; after repo {afterRepositoryBytes / 1_000_000d:F1} MB");

        bool RepositoryDone(string line) => line.StartsWith("TRAG_OPEN 1 ", StringComparison.Ordinal);
        void CheckRepositoryResponse(string line)
        {
            if (line.StartsWith("TRAG_OPEN ", StringComparison.Ordinal)
                && (!line.StartsWith("TRAG_OPEN 1 ", StringComparison.Ordinal) || !line.EndsWith(" 0", StringComparison.Ordinal)))
                throw new InvalidOperationException(line);
        }
    }

    private static double Percentile(List<double> samples) => samples.Order().ElementAt((int)Math.Ceiling(samples.Count * 0.95) - 1);
    internal static async Task SendInputAsync(Process process, CancellationToken ct)
    {
        process.Refresh();
        if (OperatingSystem.IsWindows()) PostMessage(process.MainWindowHandle, 0x100, 0x77, 0);
        else
        {
            var windows = await Xdo(["search", "--onlyvisible", "--pid", process.Id.ToString()], ct);
            _ = await Xdo(["key", "--window", windows.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0], "F8"], ct);
        }
    }
    private static async Task<string> Xdo(string[] arguments, CancellationToken ct)
    {
        var start = new ProcessStartInfo("xdotool") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Install xdotool for X11 input probing.");
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        _ = await error;
        return await output;
    }

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    private static partial int PostMessage(nint window, uint message, nuint wparam, nint lparam);
    [LibraryImport("user32.dll")]
    private static partial int IsWindowVisible(nint window);

    private sealed class ProcessCleanup(Process process) : IDisposable
    {
        public void Dispose()
        {
            try { if (!process.HasExited) { process.Kill(true); process.WaitForExit(5000); } }
            catch (InvalidOperationException) { }
        }
    }
}
