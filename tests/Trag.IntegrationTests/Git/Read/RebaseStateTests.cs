using Trag.Domain.Merge;
using Trag.Domain.Status;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// A rebase in progress as Trag reads it from real Git: the branch and where it goes, the plan with its step, the commit it
// stopped at and its message, the conflicts it left and how the resolved ones were decided; and the rebases Trag leaves to
// the user's Git tools say why.
public sealed class RebaseStateTests
{
    // feature changes f.txt three times over base; main changes the same line, so the first commit stops.
    private static async Task<GitFixture> Rebasing(params string[] options)
    {
        var f = await GitFixture.CreateAsync("main");
        try
        {
            await f.GitAsync("config", "core.autocrlf", "false");
            await f.WriteAsync("f.txt", "a\nb\nc\n"); await f.WriteAsync("g.txt", "x\n"); await f.CommitAsync("Base");
            await f.GitAsync("switch", "-c", "feature");
            await f.WriteAsync("f.txt", "a\nB-feature\nc\n"); await f.CommitAsync("Feature changes b");
            await f.WriteAsync("g.txt", "x\ny\n"); await f.CommitAsync("Feature adds y");
            await f.WriteAsync("h.txt", "h\n"); await f.CommitAsync("Feature adds h");
            await f.GitAsync("switch", "main");
            await f.WriteAsync("f.txt", "a\nB-main\nc\n"); await f.CommitAsync("Main changes b");
            await f.GitAsync("switch", "feature");
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    [Fact]
    public async Task WhenARebaseStopsOnAConflictAndItIsResolved_ShouldNameTheBranchPlanStepAndConflictsAndReadTheResolutionAsTheirs()
    {
        using var f = await Rebasing();
        Assert.Equal(1, (await f.RunAsync("rebase", "main")).ExitCode);

        var snapshot = await f.ReadAsync();
        Assert.Equal(RepositoryOperation.Rebase, snapshot.Operation);
        Assert.True(snapshot.HasSupportedRebase); Assert.True(snapshot.HasSupportedResolution);
        Assert.True(snapshot.IsDetached); Assert.False(snapshot.IsDetachedAtCommit); Assert.Equal(0, snapshot.DetachedCommits);
        var rebase = snapshot.Rebase!;
        Assert.Equal(("feature", "main", 1, 3), (rebase.Branch, rebase.OntoName, rebase.Step, rebase.Total));
        Assert.Equal(["Feature changes b"], rebase.Done.Select(commit => commit.Subject));
        Assert.Equal(["Feature adds y", "Feature adds h"], rebase.Todo.Select(commit => commit.Subject));
        Assert.Equal(rebase.Current!.Id, rebase.Stopped);
        Assert.StartsWith("Feature changes b", rebase.Message);
        Assert.False(rebase.StoppedForEdit);
        Assert.Equal([("f.txt", (int?)1)], snapshot.Resolutions.Open.Select(open => (open.Path.Display, open.Blocks)));

        // Your commit is Git's theirs in a rebase, so taking it reads as Theirs.
        await f.GitAsync("checkout", "--theirs", "f.txt"); await f.GitAsync("add", "f.txt");
        var resolved = await f.ReadAsync();
        Assert.Equal([("f.txt", Resolution.Theirs)], resolved.Resolutions.Resolved.Select(path => (path.Path.Display, path.How)));
        Assert.Empty(MergeFiles.From(resolved).Conflicts);
        // Staging touches the index, not the files Git keeps for the rebase.
        Assert.True(SnapshotComparison.SameRebase(rebase, resolved.Rebase));
    }

    // A message in another encoding, as an older tool or i18n.commitEncoding writes it, does not make the repository
    // unreadable while the rebase is stopped: its bytes are shown as they decode.
    [Fact]
    public async Task WhenTheStoppedCommitsMessageIsNotUtf8_ShouldStillReadTheRebase()
    {
        using var f = await Rebasing();
        Assert.Equal(1, (await f.RunAsync("rebase", "main")).ExitCode);
        await File.WriteAllBytesAsync(Path.Combine(f.Root, ".git", "rebase-merge", "message"), System.Text.Encoding.Latin1.GetBytes("Caf\u00e9 changes b\n"), f.Token);

        var rebase = (await f.ReadAsync()).Rebase!;

        Assert.StartsWith("Caf\ufffd changes b", rebase.Message);
    }

    // A rebase from the first commit goes onto Git's squash-onto, a commit outside the history: there is no branch to
    // name, so Git is not asked for one. Without squash-onto the same rebase has a base, and its name is read.
    [Fact]
    public async Task WhenARebaseGoesOntoNothing_ShouldNotAskGitWhichBranchItGoesOnto()
    {
        using var f = await Rebasing();
        Assert.NotEqual(0, (await f.RunAsync("rebase", "--root", "--exec", "false")).ExitCode);
        var calls = 0;
        var reader = new CliRepositoryReader(new GitProcessRunner(exited: _ => Interlocked.Increment(ref calls)));
        await reader.ReadAsync(f.Root, f.Token);
        Interlocked.Exchange(ref calls, 0);

        Assert.Equal("", (await reader.ReadAsync(f.Root, f.Token)).Rebase!.Onto);
        var ontoNothing = Interlocked.Exchange(ref calls, 0);
        File.Delete(Path.Combine(f.Root, ".git", "rebase-merge", "squash-onto"));
        Assert.NotEqual("", (await reader.ReadAsync(f.Root, f.Token)).Rebase!.Onto);

        Assert.Equal(ontoNothing + 1, calls);
    }

    [Fact]
    public async Task WhenARebaseUsesAnAutostashOrTheApplyBackend_ShouldShowItButLeaveItToGitTools()
    {
        using (var f = await Rebasing())
        {
            await f.WriteAsync("g.txt", "x\ny\nlocal\n");
            Assert.Equal(1, (await f.RunAsync("-c", "rebase.autoStash=true", "rebase", "main")).ExitCode);
            var snapshot = await f.ReadAsync();
            Assert.False(snapshot.HasSupportedRebase);
            Assert.Contains("autostash", snapshot.Rebase!.Limitation);
        }
        using (var f = await Rebasing())
        {
            Assert.NotEqual(0, (await f.RunAsync("rebase", "--apply", "--no-update-refs", "main")).ExitCode);
            var snapshot = await f.ReadAsync();
            Assert.Equal(RepositoryOperation.Rebase, snapshot.Operation);
            Assert.False(snapshot.HasSupportedRebase);
            Assert.Contains("apply backend", snapshot.Rebase!.Limitation);
        }
    }

    // Git writes a rebase's files over 4 MB on its own: the message of a commit with a long one, or a plan of some twenty
    // thousand commits. The repository still opens with its rebase, as it does with a merge message of any size.
    [Theory]
    [InlineData("message")]
    [InlineData("plan")]
    public async Task WhenRebaseStateIsLargerThanFourMegabytes_ShouldStillOpen(string story)
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("f.txt", "a\nb\nc\n"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-q", "-c", "feature");
        await f.WriteAsync("f.txt", "a\nB-feature\nc\n");
        await f.GitAsync("add", "f.txt");
        if (story == "message")
        {
            var message = Path.Combine(f.Container, "message.txt");
            await File.WriteAllTextAsync(message, "Feature changes b\n\n" + string.Concat(Enumerable.Repeat("A long explanation of the change, kept line by line.\n", 100_000)), f.Token);
            await f.GitAsync("commit", "-q", "-F", message);
        }
        else
        {
            await f.GitAsync("commit", "-q", "-m", "Feature changes b");
            var commits = string.Concat(Enumerable.Range(0, 20_000).Select(i =>
                $"commit refs/heads/feature\ncommitter Fixture <fixture@example.invalid> {1700000000 + i} +0000\ndata <<END\nFeature step {i:00000}: {new string('x', 180)}\nEND\n"
                + (i == 0 ? "from refs/heads/feature^0\n" : "") + "\n"));
            await f.Runner.RunAsync(f.Root, ["fast-import", "--quiet"], f.Token, standardInput: System.Text.Encoding.UTF8.GetBytes(commits));
        }
        await f.GitAsync("switch", "-q", "main");
        await f.WriteAsync("f.txt", "a\nB-main\nc\n"); await f.CommitAsync("Main changes b");
        await f.GitAsync("switch", "-q", "feature");
        Assert.Equal(1, (await f.RunAsync("rebase", "main")).ExitCode);
        var state = Path.Combine(f.Root, ".git", "rebase-merge");
        Assert.True(new[] { "message", "git-rebase-todo" }.Max(name => new FileInfo(Path.Combine(state, name)).Length) > 4 * 1024 * 1024);

        var snapshot = await f.ReadAsync();

        Assert.True(snapshot.HasSupportedRebase);
        Assert.StartsWith("Feature changes b", snapshot.Rebase!.Message);
    }
}
