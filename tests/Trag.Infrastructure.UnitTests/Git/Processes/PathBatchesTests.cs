using System.Text;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.UnitTests.Git.Processes;

// Paths handed to Git on its command line in batches that Windows takes whole.
public sealed class PathBatchesTests
{
    private static CancellationToken Timeout() => new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    // Each path 232 characters with spaces, which Windows quotes: more than two command lines' worth, given to Git in three batches.
    [Fact]
    public async Task WhenPathsFillMoreThanOneCommandLine_ShouldGiveGitEveryPathInBatchesWindowsTakes()
    {
        var paths = Enumerable.Range(0, 300).Select(i => $"folder {i:D3}/" + new string('n', 200) + " name with spaces.txt").ToArray();
        var runner = new GitProcessRunner();
        var given = new List<string>();

        foreach (var batch in PathBatches.Of(paths))
        {
            var result = await runner.RunAsync(Path.GetTempPath(), ["rev-parse", "--sq-quote", .. batch], Timeout());
            Assert.Equal(0, result.ExitCode);
            given.AddRange(Encoding.UTF8.GetString(result.StandardOutput).Trim().Trim('\'').Split("' '"));
        }

        Assert.Equal(paths, given);
        Assert.True(PathBatches.Of(paths).Count() > 1);
    }

    // Each path with its quotes and space counts 3,000 characters, so eight fill the budget exactly.
    [Fact]
    public void WhenPathsFillTheBudgetExactly_ShouldKeepThemInOneBatch()
    {
        var paths = Enumerable.Range(0, 8).Select(i => i + new string('x', 2996)).ToArray();

        Assert.Equal(paths, Assert.Single(PathBatches.Of(paths)));
    }

    [Fact]
    public void WhenAPathIsLongerThanABatch_ShouldGiveItAloneAndNoEmptyBatch()
    {
        var longest = new string('x', 30_000);

        Assert.Equal([[longest], ["a.txt"]], PathBatches.Of([longest, "a.txt"]));
    }
}
