using System.Diagnostics;
using System.Text;
using Trag.Infrastructure.Git.Diagnostics;

namespace Trag.Infrastructure.Git.Processes;

// Only known transfer counters reach the live UI, never arbitrary helper/hook output.
internal sealed class GitTransferProgress(IProgress<string> target)
{
    private static readonly string[] Counters = ["Enumerating objects:", "Counting objects:", "Compressing objects:", "Receiving objects:", "Resolving deltas:", "Writing objects:"];
    private readonly StringBuilder line = new(256);
    private long last;
    public void Feed(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (value is 10 or 13)
            {
                if (line.Length != 0 && Stopwatch.GetElapsedTime(last).TotalMilliseconds >= 100)
                {
                    var text = line.ToString();
                    var counter = text.StartsWith("remote: ", StringComparison.Ordinal) ? text[8..] : text;
                    if (Counters.Any(prefix => counter.StartsWith(prefix, StringComparison.Ordinal)))
                    { target.Report(GitOutputRedaction.Redact(text)); last = Stopwatch.GetTimestamp(); }
                }
                line.Clear();
            }
            else if (line.Length < 512 && value is >= 32 and < 127) line.Append((char)value);
        }
    }
}
