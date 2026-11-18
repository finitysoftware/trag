using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Trag.Benchmarks;

internal static partial class DiffUiSmoke
{
    public static async Task RunAsync(string executable, string output, CancellationToken ct, bool screenshots = true)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        var root = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        async Task Git(params string[] args)
        {
            var info = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in args) info.ArgumentList.Add(arg);
            using var git = Process.Start(info)!;
            var stdout = git.StandardOutput.ReadToEndAsync(ct); var stderr = git.StandardError.ReadToEndAsync(ct);
            await git.WaitForExitAsync(ct); _ = await stdout;
            if (git.ExitCode != 0) throw new InvalidOperationException(await stderr);
        }
        await Git("init", "-b", "main"); await Git("config", "core.autocrlf", "false");
        await Git("config", "user.name", "Trag Diff Fixture"); await Git("config", "user.email", "diff@example.invalid");
        var old = string.Concat(Enumerable.Range(1, 24_000).Select(i => $"public const int Value{i:D5} = {i}; // before\n"));
        var next = string.Concat(Enumerable.Range(1, 24_000).Select(i => $"public const int Value{i:D5} = {i + 1}; // after\n"));
        await File.WriteAllTextAsync(Path.Combine(root, "big.cs"), old, ct);
        await File.WriteAllTextAsync(Path.Combine(root, "sample.cs"), "namespace Trag;\r\n\r\npublic class Settings\r\n{\r\n\tpublic string Theme = \"light\";\r\n\tpublic int Width = 450;\r\n}\r\n", ct);
        await File.WriteAllTextAsync(Path.Combine(root, "sparse.cs"), new string('\n', 1_000_001) + "old\n", ct);
        await Git("add", "--", "big.cs", "sample.cs", "sparse.cs"); await Git("commit", "-m", "Initial diff fixture");
        await File.WriteAllTextAsync(Path.Combine(root, "big.cs"), next, ct);
        await File.WriteAllTextAsync(Path.Combine(root, "sample.cs"), "namespace Trag;\r\n\r\npublic class Settings\r\n{\r\n\t// Remember this layout\r\n\tpublic string Theme = \"dark\";\r\n\tpublic int Width = 620;\r\n\tpublic bool Split = true;\r\n}", ct);
        await File.WriteAllBytesAsync(Path.Combine(root, "binary.bin"), [0, 1, 2], ct);
        await File.WriteAllTextAsync(Path.Combine(root, "long.txt"), new string('x', 9000), ct);
        await File.WriteAllTextAsync(Path.Combine(root, "sparse.cs"), new string('\n', 1_000_001) + "new\n", ct);
        var settings = Path.Combine(output, "settings.json");
        var transcript = new List<string>(); var inputSamples = new List<double>(); var duringLoadSamples = new List<double>();
        // Process memory at named stages, so what a diff session leaves behind can be told from what the application itself needs.
        var residentSamples = new List<object>();
        await using var ui = new ProbeSession(executable, settings, transcript, ct, TimeSpan.FromSeconds(45));
        Task<string> Command(string value, string prefix = "TRAG_DIFF_UI") => ui.Command(value, prefix);
        static string[] State(string line) => line.Split(' ');
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        async Task Shot(string name) { if (screenshots) await ui.Shot(Path.Combine(output, name + ".png"), 100); }
        void Sample(string stage)
        {
            ui.Process.Refresh();
            residentSamples.Add(new { Stage = stage, ResidentBytes = ui.Process.WorkingSet64, PrivateBytes = ui.Process.PrivateMemorySize64 });
        }
        async Task<string[]> LargeState()
        {
            for (var retry = 0; retry < 80; retry++)
            {
                var state = State(await Command("diff-ui state"));
                if (int.Parse(state[2]) is > 47_000 and < 50_000) return state;
                await Task.Delay(50, ct);
            }
            await Command("screenshot " + Path.Combine(output, "failed-large.png"), "TRAG_SCREENSHOT_SAVED");
            throw new InvalidOperationException("The selected large diff did not become available.");
        }
        // A native key press on Windows, a dispatcher ping elsewhere; either way the time until the UI thread answers.
        async Task NativeInput()
        {
            var timer = Stopwatch.StartNew();
            ui.Process.Refresh();
            if (OperatingSystem.IsWindows())
            {
                if (PostMessage(ui.Process.MainWindowHandle, 0x100, 0x77, 0) == 0) throw new InvalidOperationException("Native input target is no longer available.");
            }
            else await ui.Send("ping");
            await ui.Read(OperatingSystem.IsWindows() ? "TRAG_INPUT_ACK" : "TRAG_DISPATCH_ACK"); inputSamples.Add(timer.Elapsed.TotalMilliseconds);
        }
        async Task InputDuringLoad()
        {
            for (var sample = 0; sample < 10; sample++)
            { await NativeInput(); duringLoadSamples.Add(inputSamples[^1]); await Task.Delay(5, ct); }
        }
        long resident;
        int diffRows;
        try
        {
            await Command("open " + root, "TRAG_OPEN");
            Sample("opened");
            await Command("theme dark", "TRAG_THEME_OK");
            await Command("select unstaged 3", "TRAG_SELECTION_OK");
            await Command("diff-ui mode 0"); await Shot("unified-dark");
            await Command("diff-ui mode 1"); await Shot("split-dark");
            await Command("theme light", "TRAG_THEME_OK"); await Shot("split-light");
            await Command("size 880 580", "TRAG_SIZE_OK"); await Shot("split-minimum");
            await Command("size 1400 900", "TRAG_SIZE_OK");
            await Command("select unstaged 4", "TRAG_SELECTION_OK"); await Command("diff-ui state"); await Shot("million-line");
            await Command("select unstaged 0", "TRAG_SELECTION_OK");
            await InputDuringLoad();
            var large = await LargeState();
            diffRows = int.Parse(large[2]);
            Check(int.Parse(large[2]) is > 47_000 and < 50_000, "Large diff was not fully loaded.");
            await Command("diff-ui mode 0");
            // The watcher's first reconcile reads the shown diff once more shortly after the repository opens, and a
            // report taken while that read is in flight has no rows; the place is judged once the rows are shown again.
            await Command("diff-ui go 25000");
            var anchor = await LargeState();
            Check(anchor[4] == "25000", "The selection was not placed on row 25000.");
            await Command("diff-ui mode 1");
            var split = await LargeState();
            Check(anchor[4] == split[4], "Selection lost during mode switch.");
            Check(Math.Abs(int.Parse(anchor[5]) - 24_000 - int.Parse(split[5])) <= 1, "Scroll anchor was lost.");
            Check(int.Parse(split[6]) < 100, "Diff rows were not virtualized.");
            Sample("large-split");
            await Shot("large-split");
            if (OperatingSystem.IsWindows())
            {
                // The wheel is posted at the screen point the probe reported for the rows. When it does not scroll, the
                // failure names the geometry (window origin and logical size, scaling, virtual screen), so a run at
                // another DPI leaves the numbers behind and not only the sentence.
                var geometry = await Command("window-rect", "TRAG_WINDOW_RECT");
                var screen = $"virtual screen {GetSystemMetrics(76)},{GetSystemMetrics(77)} {GetSystemMetrics(78)}x{GetSystemMetrics(79)}";
                var position = (nint)((int.Parse(split[9]) << 16) | (int.Parse(split[8]) & 0xffff));
                for (var i = 0; i < 30; i++)
                {
                    ui.Process.Refresh();
                    PostMessage(ui.Process.MainWindowHandle, 0x20A, unchecked((nuint)(uint)(-120 << 16)), position);
                    await NativeInput();
                }
                var scrolled = State(await Command("diff-ui state"));
                Check(scrolled[5] != split[5], $"Native wheel did not move the diff. Point {split[8]},{split[9]}; {geometry}; {screen}.");
                PostMessage(ui.Process.MainWindowHandle, 0x100, 0x28, 0); // Down on the focused diff list
                await NativeInput();
                Check(State(await Command("diff-ui state"))[4] != split[4], "Native keyboard selection did not move.");
            }
            await Command("diff-ui ratio 0.62"); await Command("diff-ui save");
            for (var i = 0; i < 5; i++)
            {
                await Command("select unstaged 3", "TRAG_SELECTION_OK"); await Command("diff-ui state");
                await Command("select unstaged 0", "TRAG_SELECTION_OK"); await InputDuringLoad(); await LargeState(); await NativeInput();
                Sample($"session-{i + 1}-large");
                await Command("memory", "TRAG_MEMORY");
                await Command("close", "TRAG_CLOSE");
                Sample($"session-{i + 1}-closed");
                if (i != 4) await Command("open " + root, "TRAG_OPEN");
            }
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            ui.Process.Refresh();
            if (ui.Process.HasExited) throw new InvalidOperationException($"The probe exited during idle measurement (exit {ui.Process.ExitCode}); no resident-memory result is available.");
            resident = ui.Process.WorkingSet64;
            Sample("idle-30s");
            await Command("memory", "TRAG_MEMORY");
            var exit = await ui.QuitAsync();
            var errorText = await ui.Errors;
            Check(exit == 0, errorText);
        }
        finally
        {
            await ui.DisposeAsync();
            await File.WriteAllTextAsync(Path.Combine(output, "diff-errors.log"), await ui.Errors, ct);
            await File.WriteAllLinesAsync(Path.Combine(output, "diff-transcript.log"), transcript, ct);
        }
        await using (var restarted = new ProbeSession(executable, settings, transcript, ct))
        {
            await restarted.Command("open " + root, "TRAG_OPEN");
            var restored = State(await restarted.Command("diff-ui load", "TRAG_DIFF_UI"));
            Check(restored[1] == "1" && restored[7] == "0.620", "Layout was not restored after restart.");
            await restarted.QuitAsync();
        }
        inputSamples.Sort();
        var result = new { ExecutableSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executable, ct))), FunctionalChecksPassed = true,
            DiffRows = diffRows, ScreenshotsBeforeMeasurement = screenshots, NativeInputP95Ms = inputSamples[(int)Math.Ceiling(inputSamples.Count * 0.95) - 1], NativeInputMaxMs = inputSamples.Max(),
            NativeInputSamplesMs = inputSamples, NativeInputAfterRequestSamplesMs = duringLoadSamples,
            ResidentAfterFiveDiffSessionsAnd30Seconds = resident, ResidentSamples = residentSamples, Fixture = root, RestoredLayout = true };
        await File.WriteAllTextAsync(Path.Combine(output, "diff-smoke.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), ct);
        Console.WriteLine(JsonSerializer.Serialize(result));
        if (!screenshots && resident > 100_000_000) throw new InvalidOperationException("Released memory exceeded the agreed 100 MB limit.");
    }
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    private static partial int PostMessage(nint window, uint message, nuint wparam, nint lparam);
    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);
}
