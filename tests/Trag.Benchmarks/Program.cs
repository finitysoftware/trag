using System.Diagnostics;
using Trag.Benchmarks;

// The probe answers in physical pixels; a runner Windows scales for its DPI would have its pointer moves and posted
// mouse messages scaled again, landing beside the point the probe named.
if (OperatingSystem.IsWindows()) NativeDpi.FollowMonitor();

using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(20));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; lifetime.Cancel(); };
try
{
    if (args is ["desktop-smoke", var desktopApp, var desktopOutput])
    {
        await DesktopUiSmoke.RunAsync(desktopApp, desktopOutput, lifetime.Token);
        return 0;
    }
    if (args is ["diff-smoke" or "diff-memory", var diffApp, var diffOutput])
    {
        await DiffUiSmoke.RunAsync(diffApp, diffOutput, lifetime.Token, screenshots: args[0] == "diff-smoke");
        return 0;
    }
    if (args is ["accounts-smoke", var accountApp, var accountOutput])
    {
        await AccountUiSmoke.RunAsync(accountApp, accountOutput, lifetime.Token);
        return 0;
    }
    if (args is ["merge-smoke", var mergeApp, var mergeOutput])
    {
        await MergeUiSmoke.RunAsync(mergeApp, mergeOutput, lifetime.Token);
        return 0;
    }
    if (args is ["sync-smoke", var syncApp, var syncOutput])
    {
        await SyncUiSmoke.RunAsync(syncApp, syncOutput, lifetime.Token);
        return 0;
    }
    if (args is ["history-smoke" or "history-memory", var historyApp, var historyOutput, var largeHistory])
    {
        await HistoryUiSmoke.RunAsync(historyApp, historyOutput, largeHistory, lifetime.Token, screenshots: args[0] == "history-smoke");
        return 0;
    }
    if (args is ["write-smoke", var writeApp, var writeOutput])
    {
        await RepositoryWriteUiSmoke.RunAsync(writeApp, writeOutput, lifetime.Token);
        return 0;
    }
    if (args is ["live-smoke", var liveApp, var liveOutput])
    {
        await RepositoryUiSmoke.RunAsync(liveApp, liveOutput, lifetime.Token, live: true);
        return 0;
    }
    if (args is ["open-smoke", var openApp, var openOutput])
    {
        await RepositoryUiSmoke.RunAsync(openApp, openOutput, lifetime.Token);
        return 0;
    }
    if (args is ["ui-open", var sessionApp, var sessionOutput, var sessionCount, var sessionRepository])
    {
        await UiBenchmark.MeasureAsync(sessionApp, sessionOutput, int.Parse(sessionCount), sessionRepository, lifetime.Token);
        return 0;
    }
    if (args is ["generate", var root, var commits, var files])
    {
        await FixtureGenerator.GenerateAsync(root, int.Parse(commits), int.Parse(files), lifetime.Token);
        return 0;
    }
    if (args is ["smoke", var executable])
    {
        foreach (var command in new[] { "--ui-smoke", "--ui-auth-smoke", "--auth-probe", "--account-probe", "--vault-probe", "--process-probe" })
        {
            using var process = Start(executable, [command]);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            using var registration = timeout.Token.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            Console.WriteLine($"{command}: exit {process.ExitCode}; {(await output).Trim()}");
            _ = await errors;
            if (process.ExitCode != 0 && !(command == "--vault-probe" && process.ExitCode == 3)) return 1;
        }
        return 0;
    }
    if (args is ["ui", var app, var outputFile, var runCount])
    {
        await UiBenchmark.MeasureAsync(app, outputFile, int.Parse(runCount), null, lifetime.Token);
        return 0;
    }
    Console.Error.WriteLine("Usage: generate <new-dir> <commits> <files> | smoke <published-app> | open-smoke <published-app> <output-directory> | ui <published-app> <result.json> <runs> | ui-open <published-app> <result.json> <runs> <fixture>");
    return 2;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.ToString());
    return 1;
}

static Process Start(string executable, string[] arguments)
{
    var start = new ProcessStartInfo(Path.GetFullPath(executable))
    {
        UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        RedirectStandardInput = true, StandardInputEncoding = new System.Text.UTF8Encoding(false)
    };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    return Process.Start(start) ?? throw new InvalidOperationException("Unable to start published app.");
}

internal static partial class NativeDpi
{
    public static void FollowMonitor() => SetProcessDpiAwarenessContext(-4);
    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    private static partial int SetProcessDpiAwarenessContext(nint context);
}
