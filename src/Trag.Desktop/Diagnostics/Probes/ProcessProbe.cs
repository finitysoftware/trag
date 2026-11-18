using System.Diagnostics;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Desktop.Diagnostics.Probes;

// Cancelling a Git command must end the Git process and its shell child promptly.
internal static class ProcessProbe
{
    public static async Task<int> RunAsync()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var timer = Stopwatch.StartNew();
        try
        {
            await new GitProcessRunner().RunAsync(Path.GetTempPath(), ["-c", "alias.tragwait=!sleep 30", "tragwait"], cancellation.Token).ConfigureAwait(false);
            return 1;
        }
        catch (OperationCanceledException)
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(5)) return 1;
            ProbeReport.Write("process-cancellation", $"Git and shell child canceled in {timer.Elapsed.TotalMilliseconds:F1} ms");
            return 0;
        }
    }
}
