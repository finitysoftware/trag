using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Processes;

// The exact arrays and variables each runner sent to Git before they were gathered into one place. These are recorded
// values, not preferences: a change here is a change to what Git is told, and has to be argued for on its own.
public sealed class LocalGitPresetTests
{
    [Fact]
    public void WhenTheWorkingTreeIsRead_ShouldRefuseTheNetworkAndLetTheMonitorAnswer()
    {
        Assert.Equal(["status", "--porcelain=v2"], LocalGitPreset.Reading.Arguments(["status", "--porcelain=v2"]));
        Assert.Equal(new Dictionary<string, string?> { ["GIT_NO_LAZY_FETCH"] = "1", ["GIT_ALLOW_PROTOCOL"] = "" }, LocalGitPreset.Reading.Variables);
    }

    // A write must see the real state, not the monitor's answer.
    [Fact]
    public void WhenTheRepositoryIsWritten_ShouldAlsoRefuseTheMonitor()
    {
        Assert.Equal(["-c", "core.fsmonitor=false", "commit", "--file=-"], LocalGitPreset.Writing.Arguments(["commit", "--file=-"]));
        Assert.Equal(new Dictionary<string, string?> { ["GIT_NO_LAZY_FETCH"] = "1", ["GIT_ALLOW_PROTOCOL"] = "" }, LocalGitPreset.Writing.Variables);
    }

    [Fact]
    public void WhenTheCallerPassesItsOwnConfiguration_ShouldPutThePresetBeforeIt() =>
        // A caller's own -c would otherwise be overridden by the preset instead of the other way round.
        Assert.Equal(["-c", "core.fsmonitor=false", "-c", "merge.ff=false", "merge"],
            LocalGitPreset.Writing.Arguments(["-c", "merge.ff=false", "merge"]));

    // From Git 2.45 on, GIT_NO_LAZY_FETCH answers a version a partial clone lacks as missing without starting a fetch.
    // The empty GIT_ALLOW_PROTOCOL alone refuses that fetch only once a git fetch runs, a whole process more for every
    // such read. An older Git knows no such variable, so there is nothing to hold.
    [Fact]
    public async Task WhenAReadAsksForAVersionAPartialCloneLacks_ShouldNotStartAFetchForIt()
    {
        using var f = await GitFixture.CreateAsync();
        if (GitVersion.Parse(await f.GitAsync("--version")) < new Version(2, 45)) return;
        await f.WriteAsync("a.txt", "one\n"); await f.CommitAsync("One");
        await f.WriteAsync("a.txt", "one\ntwo\n"); await f.CommitAsync("Two");
        await f.GitAsync("config", "uploadpack.allowFilter", "true");
        var clone = Path.Combine(f.Container, "partial");
        await f.GitAtAsync(f.Container, "clone", "--filter=blob:none", "--", new Uri(f.Root).AbsoluteUri, clone);
        var trace = Path.Combine(f.Container, "trace.json");

        await new PresetGitRunner(f.Runner, LocalGitPreset.Writing).ResultAsync(clone, ["cat-file", "-e", "HEAD~1:a.txt"], f.Token,
            new Dictionary<string, string?> { ["GIT_TRACE2_EVENT"] = trace });

        Assert.DoesNotContain(File.ReadLines(trace), line => line.Contains("\"child_start\"", StringComparison.Ordinal) && line.Contains("\"fetch\"", StringComparison.Ordinal));
    }
}
