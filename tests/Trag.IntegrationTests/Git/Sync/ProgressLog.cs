namespace Trag.IntegrationTests.Git.Sync;

// Every progress line a sync reported, in order, as it reported them.
internal sealed class ProgressLog : IProgress<string>
{
    public List<string> Reports { get; } = [];
    public void Report(string value) { lock (Reports) Reports.Add(value); }
}
