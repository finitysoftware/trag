using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.UnitTests.Git.State;

// A rebase in progress read from the files Git keeps in rebase-merge, as Git 2.55.0 left them.
public sealed class RebaseMetadataReaderTests : IDisposable
{
    private const string A = "0a8644ccd5a8cfe9933583c50752dbd5f098a008";
    private const string X = "6016e9e1381381c24af2a73f20729d8402731bd1";
    private const string C = "3eaaa539a5b9e3766e8e2a2c9f45bd7de79a1bfc";
    private const string D = "173cc4bf906aab044ac94b88ed835a2bd84fa101";
    private readonly string gitDirectory = Path.Combine(Path.GetTempPath(), "trag-rebase-state-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(gitDirectory)) Directory.Delete(gitDirectory, true); }

    // Git's msgnum and end count every line of the plan, the exec lines a rewrite amends its messages with too, while the
    // rebase view lists the commits: a stop after one exec line is at commit 3 of 4, not at Git's line 4 of 6. A fold Git
    // could not commit is in both lists, the done one and the one put back, and is counted once.
    [Theory]
    [InlineData("4", "6", "pick " + A + " # A\nexec true\ndrop " + X + " # X\npick " + C + " # C\n", "exec true\npick " + D + " # D\n", null, 3, 4)]
    [InlineData("3", "4", "pick " + A + " # A\nexec touch flag\nfixup " + X + " # X\n", "fixup " + X + " # X\npick " + D + " # C\n", "fixup " + X + "\n", 2, 3)]
    public async Task WhenATragRewriteStops_ShouldCountOnlyThePlannedCommits(string msgnum, string end, string done, string todo, string? fixups, int step, int total)
    {
        var directory = Directory.CreateDirectory(Path.Combine(gitDirectory, "rebase-merge")).FullName;
        File.WriteAllText(Path.Combine(directory, "head-name"), "refs/heads/main\n");
        File.WriteAllText(Path.Combine(directory, "onto"), A + "\n");
        File.WriteAllText(Path.Combine(directory, "orig-head"), D + "\n");
        File.WriteAllText(Path.Combine(directory, "msgnum"), msgnum + "\n");
        File.WriteAllText(Path.Combine(directory, "end"), end + "\n");
        File.WriteAllText(Path.Combine(directory, "done"), done);
        File.WriteAllText(Path.Combine(directory, "git-rebase-todo"), todo);
        if (fixups is null) File.WriteAllText(Path.Combine(directory, "message"), "C\n");
        else File.WriteAllText(Path.Combine(directory, "current-fixups"), fixups);

        var rebase = await RebaseMetadataReader.ReadAsync(gitDirectory, RootRefs.FromFiles(gitDirectory), CancellationToken.None);

        Assert.Equal((step, total), (rebase!.Step, rebase.Total));
    }

    // A fold Git could not commit comes in two shapes. Git puts it back at the top of the plan without a message file when
    // a file stands in its way, and up to 2.55 also when the commit fails, as when the key cannot sign it. From 2.56 a
    // failed commit leaves the fold done and stops to amend it, with the message Git prepared for it: no edit stop, and
    // the plan after it holds no fold of it that could repeat its message or have been changed away.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenGitCouldNotCommitAFold_ShouldTellWhetherItPutTheFoldBackInThePlan(bool putBack)
    {
        var directory = Directory.CreateDirectory(Path.Combine(gitDirectory, "rebase-merge")).FullName;
        File.WriteAllText(Path.Combine(directory, "head-name"), "refs/heads/main\n");
        File.WriteAllText(Path.Combine(directory, "onto"), A + "\n");
        File.WriteAllText(Path.Combine(directory, "orig-head"), D + "\n");
        File.WriteAllText(Path.Combine(directory, "msgnum"), "2\n");
        File.WriteAllText(Path.Combine(directory, "end"), "3\n");
        File.WriteAllText(Path.Combine(directory, "done"), "pick " + A + " # A\nsquash " + X + " # X\n");
        File.WriteAllText(Path.Combine(directory, "git-rebase-todo"), (putBack ? "squash " + X + " # X\n" : "") + "squash " + D + " # D\n");
        File.WriteAllText(Path.Combine(directory, "current-fixups"), "squash " + X + "\n");
        if (!putBack)
        {
            File.WriteAllText(Path.Combine(directory, "message"), "# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nA\n\n# This is the commit message #2:\n\nX\n");
            File.WriteAllText(Path.Combine(directory, "amend"), C + "\n");
            File.WriteAllText(Path.Combine(directory, "stopped-sha"), X + "\n");
        }

        var rebase = (await RebaseMetadataReader.ReadAsync(gitDirectory, RootRefs.FromFiles(gitDirectory), CancellationToken.None))!;

        Assert.Equal((true, putBack, false, putBack, false, 2, 3),
            (rebase.FoldNotCommitted, rebase.FoldPutBack, rebase.StoppedForEdit, rebase.FoldRepeatsMessage, rebase.FoldPlanChanged, rebase.Step, rebase.Total));
    }

    // git am keeps its state in rebase-apply too, marked by the applying file: that is a patch application, not a rebase.
    [Fact]
    public async Task WhenRebaseApplyIsApplyingAMailbox_ShouldReadNoRebase()
    {
        var directory = Directory.CreateDirectory(Path.Combine(gitDirectory, "rebase-apply")).FullName;
        File.WriteAllText(Path.Combine(directory, "applying"), "");

        Assert.Null(await RebaseMetadataReader.ReadAsync(gitDirectory, RootRefs.FromFiles(gitDirectory), CancellationToken.None));
    }
}
