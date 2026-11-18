using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Trag.Benchmarks;

// One published Trag started in probe mode: commands go in on standard input, TRAG_* lines come back on standard
// output, and every line read is kept in the transcript. The suites differ only in what they send and check.
internal sealed class ProbeSession : IAsyncDisposable
{
    private readonly List<string> transcript;
    private readonly CancellationToken ct;
    private readonly TimeSpan? commandTimeout;
    private readonly Task<string> errors;
    private bool disposed;

    public Process Process { get; }

    // settings is the TRAG_PROBE_SETTINGS file the UI keeps its layout and accounts in for this run; commandTimeout
    // bounds every single command on top of the run's own token.
    public ProbeSession(string executable, string settings, List<string> transcript, CancellationToken ct, TimeSpan? commandTimeout = null)
    {
        this.transcript = transcript; this.ct = ct; this.commandTimeout = commandTimeout;
        var start = new ProcessStartInfo(Path.GetFullPath(executable))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false)
        };
        start.ArgumentList.Add("--ui-probe"); start.Environment["TRAG_PROBE_SETTINGS"] = settings;
        Process = Process.Start(start) ?? throw new InvalidOperationException("UI did not start.");
        errors = Process.StandardError.ReadToEndAsync(ct);
    }

    // Everything the UI wrote to standard error, complete once the process has exited.
    public Task<string> Errors => errors;

    public Task Send(string command) => Process.StandardInput.WriteLineAsync(command.AsMemory(), ct);

    // Reads until a line with the prefix; a line ending in _DISABLED is a refusal, a TRAG_PROBE_ERROR line a command the
    // UI could not read, and the UI ending first is a failure.
    public async Task<string> Read(string prefix, string? command = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (commandTimeout is { } limit) timeout.CancelAfter(limit);
        while (await Process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
        {
            transcript.Add(line);
            if (line.EndsWith("_DISABLED", StringComparison.Ordinal) || line.StartsWith("TRAG_PROBE_ERROR", StringComparison.Ordinal)) throw new InvalidOperationException((command ?? prefix) + ": " + line);
            // The reply's first word, whole: TRAG_MERGE is not the TRAG_MERGE_FINISHED an earlier action prints when it ends.
            if (line == prefix || line.StartsWith(prefix + ' ', StringComparison.Ordinal)) return line;
        }
        throw new InvalidOperationException("UI ended before " + prefix);
    }

    public async Task<string> Command(string command, string prefix)
    {
        await Send(command);
        return await Read(prefix, command);
    }

    public async Task Expect(string command, string expected)
    {
        var actual = await Command(command, expected.Split(' ')[0]);
        if (actual != expected) throw new InvalidOperationException($"Expected {expected}, got {actual}");
    }

    // The UI settles for a moment first, so a layout that has just changed is what the picture shows.
    public async Task Shot(string path, int settleMilliseconds)
    {
        await Task.Delay(settleMilliseconds, ct);
        await Expect("screenshot " + path, "TRAG_SCREENSHOT_SAVED");
    }

    // The compact window reports its own bounds in the logical units a screenshot has, whatever the display scaling;
    // the drawn title bar makes the minimum height taller than the requested 580.
    public async Task ExpectCompactScreenshot(string path)
    {
        var rect = (await Command("window-rect", "TRAG_WINDOW_RECT")).Split(' ');
        var bytes = await File.ReadAllBytesAsync(path, ct);
        if (BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)) != 880 || rect[3] != "880"
            || BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)).ToString(CultureInfo.InvariantCulture) != rect[4])
            throw new InvalidOperationException("Compact screenshot size mismatch.");
    }

    // One native key press, timed until the UI acknowledges it.
    public async Task<double> Input(TimeSpan? timeout = null)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout is { } duration) limit.CancelAfter(duration);
        var watch = Stopwatch.StartNew();
        await UiBenchmark.SendInputAsync(Process, limit.Token);
        while (await Process.StandardOutput.ReadLineAsync(limit.Token) is { } line)
        {
            transcript.Add(line);
            if (line == "TRAG_INPUT_ACK") return watch.Elapsed.TotalMilliseconds;
        }
        throw new InvalidOperationException("Native input was not acknowledged.");
    }

    // Repeats a command until it answers exactly as expected, for a state the UI reaches on its own.
    public async Task Until(string command, string expected, TimeSpan timeout, int pollMilliseconds)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            if (await Command(command, expected.Split(' ')[0]) == expected) return;
            await Task.Delay(pollMilliseconds, ct);
        }
        throw new InvalidOperationException("Timed out: " + expected);
    }

    public long Resident()
    {
        Process.Refresh();
        return Process.WorkingSet64;
    }

    // Asks the UI to quit and answers its exit code.
    public async Task<int> QuitAsync()
    {
        await Send("quit");
        await Process.WaitForExitAsync(ct);
        return Process.ExitCode;
    }

    // A hook or script signals that it has started by creating a marker file; the suite waits for the marker, not for time.
    public static async Task WaitForFile(string path, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (!File.Exists(path)) await Task.Delay(20, timeout.Token);
    }

    // A session still running is asked to quit and given five seconds, then killed with its children. A suite may
    // dispose early to have the errors complete for its log; the using block's dispose then does nothing.
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (!Process.HasExited)
            {
                await Process.StandardInput.WriteLineAsync("quit");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await Process.WaitForExitAsync(timeout.Token);
            }
        }
        catch (Exception) { if (!Process.HasExited) { Process.Kill(true); await Process.WaitForExitAsync(CancellationToken.None); } }
        try { transcript.Add("UI exit " + Process.ExitCode + ": " + await errors); }
        catch (OperationCanceledException) { transcript.Add("UI exit " + Process.ExitCode); }
        Process.Dispose();
    }
}
