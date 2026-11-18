using System.Diagnostics;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// The reads the shell's Cancel does not reach (the branch, tag and stash lists, the merge and rebase previews,
// the range of a rewrite and the repository's details) end at their own limit when Git never answers, as a timeout and
// not as a cancel the user made, and not before that limit. All of them wait together, so the test takes as long as the
// longest limit; under a loaded suite a read may end well after its limit, which is not what this test holds.
public sealed class HungGitReadLimitTests
{
    [Fact]
    public async Task WhenGitNeverAnswersAReadCancelDoesNotReach_ShouldEndTheReadAtItsOwnLimit()
    {
        using var f = await GitFixture.CreateAsync("main", scenarioTimeout: TimeSpan.FromMinutes(5));
        await f.WriteAsync("f.txt", "a\n");
        await f.CommitAsync("Base");
        var commit = await f.GitAsync("rev-parse", "HEAD");
        var hung = await HungGit.CreateAsync(f.Container, f.Token);
        var runner = new GitProcessRunner(executable: new() { Path = hung.Program });
        var branches = new CliBranchReader(runner);
        var thirtySeconds = TimeSpan.FromSeconds(30);
        (string Name, TimeSpan Limit, Func<Task> Read)[] reads =
        [
            ("branch list", thirtySeconds, () => branches.ReadBranchesAsync(f.Root, f.Token)),
            ("tag list", thirtySeconds, () => branches.ReadTagsAsync(f.Root, f.Token)),
            ("stash list", thirtySeconds, () => new CliStashReader(runner).ReadStashesAsync(f.Root, f.Token)),
            ("merge preview", thirtySeconds, () => branches.PreviewMergeAsync(f.Root, "refs/heads/main", "refs/heads/main", f.Token)),
            ("rebase preview", thirtySeconds, () => branches.PreviewRebaseAsync(f.Root, "refs/heads/main", f.Token)),
            ("rewrite range", thirtySeconds, () => branches.ReadRewriteAsync(f.Root, commit, f.Token)),
            ("repository details", TimeSpan.FromMinutes(2), () => new CliHistoryReader(runner).ReadDetailsAsync(f.Root, false, false, f.Token)),
        ];

        var ended = await Task.WhenAll(reads.Select(async read =>
        {
            var watch = Stopwatch.StartNew();
            var error = await Record.ExceptionAsync(read.Read);
            return read.Name + ": " + error?.GetType().Name + (watch.Elapsed >= read.Limit ? " at its limit" : " after " + watch.Elapsed);
        }));

        Assert.Equal(reads.Select(read => read.Name + ": " + nameof(TimeoutException) + " at its limit"), ended);
    }
}
