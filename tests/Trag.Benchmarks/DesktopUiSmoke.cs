using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Trag.Benchmarks;

internal static partial class DesktopUiSmoke
{
    public static async Task RunAsync(string executable, string output, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native desktop pointer probe requires Windows.");
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        var root = Path.Combine(output, "desktop-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        async Task Git(params string[] args)
        {
            var info = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in args) info.ArgumentList.Add(arg);
            using var git = Process.Start(info)!;
            var stdout = git.StandardOutput.ReadToEndAsync(ct); var stderr = git.StandardError.ReadToEndAsync(ct);
            await git.WaitForExitAsync(ct); _ = await stdout;
            if (git.ExitCode != 0) throw new IOException(await stderr);
        }
        await Git("init", "-b", "main"); await Git("config", "user.name", "Trag Desktop Fixture"); await Git("config", "user.email", "desktop@example.invalid");
        await Git("config", "core.autocrlf", "false");
        var sample = Path.Combine(root, "Settings.cs");
        var original = "namespace Trag;\n\npublic class Settings\n{\n    public string Theme = \"system\";\n    public bool RememberLayout = false;\n}\n";
        await File.WriteAllTextAsync(sample, original, ct); await Git("add", "--", "Settings.cs"); await Git("commit", "-m", "Add application settings");
        foreach (var title in new[] { "Align toolbar actions", "Improve keyboard navigation", "Show branch sync counts", "Preserve file selection", "Refine dark theme", "Keep commit details readable" })
            await Git("commit", "--allow-empty", "-m", title);
        await File.WriteAllTextAsync(sample, original.Replace("false", "true", StringComparison.Ordinal), ct); await Git("add", "--", "Settings.cs"); await Git("commit", "-m", "Remember the desktop layout");
        await File.WriteAllTextAsync(sample, original.Replace("system", "dark", StringComparison.Ordinal).Replace("false", "true", StringComparison.Ordinal), ct);
        var transcript = new List<string>();
        var settings = Path.Combine(output, "settings.json");
        GetCursorPos(out var originalPointer); NativePoint? lastPointer = null;
        await using var ui = new ProbeSession(executable, settings, transcript, ct, TimeSpan.FromSeconds(45));
        async Task<string[]> Control(string name) => (await ui.Command("desktop-control " + name, "TRAG_CONTROL")).Split(' ');
        static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);
        static void Check(bool result, string message) { if (!result) throw new InvalidOperationException(message); }
        void Message(uint message, nuint key, int x = 0, int y = 0)
        {
            ui.Process.Refresh();
            if (PostMessage(ui.Process.MainWindowHandle, message, key, (nint)((y << 16) | (x & 0xffff))) == 0) throw new IOException("Native input target unavailable.");
        }
        async Task<string[]> Drag(string name, int dx, int dy)
        {
            var before = await Control(name); var x = int.Parse(before[2]); var y = int.Parse(before[3]);
            ui.Process.Refresh(); var handle = ui.Process.MainWindowHandle;
            SetForegroundWindow(handle); await Task.Delay(60, ct);
            Check(GetClientRect(handle, out var bounds) != 0, "The probe window has no client bounds.");
            var origin = new NativePoint(); Check(ClientToScreen(handle, ref origin) != 0, "The probe origin is unavailable.");
            var endX = Math.Clamp(x + dx, 1, bounds.Right - 2); var endY = Math.Clamp(y + dy, 1, bounds.Bottom - 2);
            void Move(int a, int b)
            {
                Check(GetForegroundWindow() == handle, "Native pointer probe interrupted by a focus change.");
                Check(SetCursorPos(origin.X + a, origin.Y + b) != 0, "Native pointer movement failed.");
                lastPointer = new() { X = origin.X + a, Y = origin.Y + b };
            }
            // Real pointer movement keeps Windows' cursor position and Avalonia's capture state consistent.
            // Posting synthetic WM_MOUSEMOVE alone can interleave with a real cursor update after layout.
            // Windows can reject foreground activation from a background runner. Expose only our
            // new probe window, then let a real click activate it after verifying the hit target.
            SetWindowPos(handle, -1, 0, 0, 0, 0, 0x13);
            try
            {
                var start = new NativePoint { X = origin.X + x, Y = origin.Y + y };
                SetCursorPos(start.X, start.Y); lastPointer = start; await Task.Delay(60, ct);
                Check(GetAncestor(WindowFromPoint(start), 2) == handle, "The pointer target is not the probe window.");
                MouseEvent(0x2, 0, 0, 0, 0);
                await Task.Delay(50, ct);
                SetWindowPos(handle, -2, 0, 0, 0, 0, 0x13);
                for (var i = 1; i <= 5; i++) { Move(x + (endX - x) * i / 5, y + (endY - y) * i / 5); await Task.Delay(35, ct); }
            }
            finally { MouseEvent(0x4, 0, 0, 0, 0); SetWindowPos(handle, -2, 0, 0, 0, 0, 0x13); }
            await Task.Delay(60, ct);
            return await Control(name);
        }
        async Task Restore(string name)
        {
            Message(0x100, 0x0D); Message(0x101, 0x0D); await Task.Delay(70, ct);
            Check(Math.Abs(Number((await Control(name))[6]) - 0.5) < 0.001, "Keyboard restore failed: " + name);
        }
        Task Shot(string name) => ui.Shot(Path.Combine(output, name + ".png"), 100);
        try
        {
            await ui.Command("open " + root, "TRAG_OPEN"); await ui.Command("theme dark", "TRAG_THEME_OK"); await Shot("status-empty-dark");
            await ui.Command("select unstaged 0", "TRAG_SELECTION_OK"); await ui.Command("diff-ui mode 1", "TRAG_DIFF_UI"); await Shot("status-dark");
            var before = await Control("StatusFilesDivider"); var moved = await Drag("StatusFilesDivider", 100, 0);
            Check(Number(moved[6]) > Number(before[6]), "Native horizontal drag did not resize the status panes.");
            Check(Number((await Drag("StatusFilesDivider", -4000, 0))[6]) == 0, "Status left edge did not collapse.");
            Check(Number((await Drag("StatusFilesDivider", 4000, 0))[6]) == 1, "Status right edge did not collapse."); await Restore("StatusFilesDivider");
            await ui.Command("history", "TRAG_HISTORY"); await Shot("history-empty-dark");
            await ui.Command("history-select 0", "TRAG_COMMIT"); await ui.Command("history-file 0", "TRAG_COMMIT_DIFF"); await Shot("history-dark");
            before = await Control("HistoryDivider"); moved = await Drag("HistoryDivider", 0, -70);
            Check(Number(moved[8]) > Number(before[8]) + 40, "Native vertical drag did not enlarge commit details.");
            Check(Number((await Drag("HistoryDivider", 0, 4000))[8]) <= 1, "Bottom commit pane did not hide."); await Shot("history-hidden");
            Check(Number((await Drag("HistoryDivider", 0, -4000))[7]) <= 1, "Top history pane did not hide."); await Shot("history-expanded"); await Restore("HistoryDivider");
            before = await Control("HistoryFilesDivider"); moved = await Drag("HistoryFilesDivider", -65, 0);
            Check(Number(moved[6]) < Number(before[6]), "Native horizontal drag did not resize history files.");
            Check(Number((await Drag("HistoryFilesDivider", -4000, 0))[6]) == 0, "History left edge did not collapse.");
            Check(Number((await Drag("HistoryFilesDivider", 4000, 0))[6]) == 1, "History right edge did not collapse."); await Restore("HistoryFilesDivider");
            await ui.Command("theme light", "TRAG_THEME_OK"); await Shot("history-light");
            await ui.Command("size 880 580", "TRAG_SIZE_OK"); await Shot("history-minimum");
            await ui.Command("size 1400 900", "TRAG_SIZE_OK"); await Shot("history-large");
            await ui.Command("diff-ui save", "TRAG_DIFF_UI");
            var state = await ui.Command("desktop-state", "TRAG_DESKTOP"); transcript.Add("Final " + state);
            await ui.QuitAsync();
        }
        finally
        {
            if (lastPointer is { } last && GetCursorPos(out var current) != 0 && current.X == last.X && current.Y == last.Y)
                SetCursorPos(originalPointer.X, originalPointer.Y);
            await ui.DisposeAsync();
            await File.WriteAllTextAsync(Path.Combine(output, "desktop-errors.log"), await ui.Errors, ct);
            await File.WriteAllLinesAsync(Path.Combine(output, "desktop-transcript.log"), transcript, ct);
        }
        await using (var restarted = new ProbeSession(executable, settings, transcript, ct, TimeSpan.FromSeconds(45)))
        {
            await restarted.Command("diff-ui load", "TRAG_DIFF_UI");
            var state = await restarted.Command("desktop-state", "TRAG_DESKTOP");
            var parts = state.Split(' '); Check(parts[2] == "1" && parts[4] == "Light" && Number(parts[6]) == 0.5 && Number(parts[7]) == 0.5, "Theme and pane layout were not restored.");
            transcript.Add("Restored " + state);
            await restarted.QuitAsync();
            await File.WriteAllTextAsync(Path.Combine(output, "desktop-restart-errors.log"), await restarted.Errors, ct);
        }
        await File.WriteAllLinesAsync(Path.Combine(output, "desktop-transcript.log"), transcript, ct);
        var result = new { ExecutableSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executable, ct))), NativePointerChecksPassed = true, RestoredThemeAndLayout = true, Fixture = root, Platform = Environment.OSVersion.ToString() };
        await File.WriteAllTextAsync(Path.Combine(output, "desktop-smoke.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), ct);
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    private static partial int PostMessage(nint window, uint message, nuint wparam, nint lparam);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [LibraryImport("user32.dll")] private static partial int GetCursorPos(out NativePoint point);
    [LibraryImport("user32.dll")] private static partial int SetCursorPos(int x, int y);
    [LibraryImport("user32.dll")] private static partial int ClientToScreen(nint window, ref NativePoint point);
    [LibraryImport("user32.dll")] private static partial int GetClientRect(nint window, out NativeRect rect);
    [LibraryImport("user32.dll")] private static partial int SetForegroundWindow(nint window);
    [LibraryImport("user32.dll")] private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] private static partial nint WindowFromPoint(NativePoint point);
    [LibraryImport("user32.dll")] private static partial nint GetAncestor(nint window, uint flags);
    [LibraryImport("user32.dll")] private static partial int SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [LibraryImport("user32.dll", EntryPoint = "mouse_event")] private static partial void MouseEvent(uint flags, uint x, uint y, uint data, nuint extra);
}
