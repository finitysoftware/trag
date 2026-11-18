using System.Text;
using Trag.Application.Workspace;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// The interruption matrix of the writes in WriterInterruptionRows: each write runs through the workspace on real Git and
// is stopped where the stop is deterministic. Cancel comes once the n-th Git process the write started has exited, for
// every n of a write nobody stops, which covers a stop before Git starts, after Git exits and during the verification;
// or while Git holds the locks of the refs it updates; or while Git writes z.txt. Whatever stopped it, what the write
// reports has to be what the repository shows: Cancelled leaves the repository as it was, with a sentence the write may
// say; Succeeded or NeedsResolution leaves it as the write nobody stopped does; Uncertain asks for a review and is always
// true; Rejected never is. The repository keeps no lock file, git fsck finds nothing wrong, and a Trag recovery index is
// there exactly when the outcome names one. Each run works in a copy of the row's repository, so every n starts from
// the same bytes. The rows are split by family into classes that run side by side.
internal static class WriterInterruption
{
    private const string RefsArmed = "trag-refs-arm";
    private const string RefsReady = "trag-refs-ready";
    private const string SmudgeArmed = "trag-smudge-arm";
    private const string SmudgeReady = "trag-smudge-ready";
    private static readonly string SilentGit = Path.Combine(AppContext.BaseDirectory, "Trag.GitStub" + (OperatingSystem.IsWindows() ? ".exe" : ""));

    public static async Task StopAfterEveryGitAsync(string row)
    {
        using var template = await GitFixture.CreateAsync("main");
        var build = await PrepareAsync(template, row);
        var (before, done, processes) = await ReferenceAsync(template, row, build);
        var problems = new List<string>();

        for (var stop = 1; stop <= processes; stop++)
        {
            using var f = await CopyAsync(template);
            var (result, _) = await WriteAsync(f, build, stop, null);
            problems.AddRange((await JudgeAsync(f, row, result, before, done)).Select(problem => "after Git " + stop + " of " + processes + ": " + problem));
        }

        Assert.True(problems.Count == 0, row + "\n" + string.Join("\n", problems));
    }

    // A reference-transaction hook holds the first transaction that reaches its prepared state, as a slow disk would, or
    // the first one that updates the ref named held.
    public static Task StopWhileGitHoldsTheRefLocksAsync(string row, string? held) => StopAtGateAsync(row, RefsArmed, RefsReady, held ?? "");

    // The write run once by a Git that skips every command that writes and exits 0 as if it had done it (Trag.GitStub),
    // with the commands that read going to the real Git. What Trag writes itself still happens, so the write may report
    // that it happened only when the repository shows what the write nobody stopped leaves; otherwise it says that Git
    // refused it or that it could not be verified, never that Cancel stopped it, and it leaves nothing behind.
    public static async Task SkipEveryGitWriteAsync(string row)
    {
        using var template = await GitFixture.CreateAsync("main");
        var build = await PrepareAsync(template, row);
        var (_, done, _) = await ReferenceAsync(template, row, build);
        using var f = await CopyAsync(template);
        await using var workspace = new RepositoryWorkspace(new CliRepositoryReader(f.Runner),
            writer: new GitWriteRouter(new GitProcessRunner(executable: new() { Path = SilentGit })));
        var shown = await workspace.OpenAsync(f.Root, f.Token);

        var result = await workspace.WriteAsync(build(shown!), f.Token);

        var problems = await LeftoversAsync(f, result);
        var after = await RepositoryFingerprint.ReadAsync(f);
        if (result.Outcome == GitWriteOutcome.Cancelled) problems.Add(Said(result, "nothing canceled the write"));
        if (result.Outcome is GitWriteOutcome.Succeeded or GitWriteOutcome.NeedsResolution && !after.Content.SequenceEqual(done.Content))
            problems.Add(Said(result, "Git skipped the write: " + RepositoryFingerprint.Difference(done.Content, after.Content)));
        Assert.True(problems.Count == 0, row + "\n" + string.Join("\n", problems));
    }

    // The smudge filter of z.txt waits once, while Git writes the working tree.
    public static Task StopWhileGitWritesTheWorkingTreeAsync(string row) => StopAtGateAsync(row, SmudgeArmed, SmudgeReady, "");

    private static async Task StopAtGateAsync(string row, string armed, string ready, string held)
    {
        using var template = await GitFixture.CreateAsync("main");
        var build = await PrepareAsync(template, row);
        if (armed == RefsArmed) await HoldRefsAsync(template);
        var (before, done, _) = await ReferenceAsync(template, row, build);
        using var f = await CopyAsync(template);

        var (result, _) = await WriteAsync(f, build, null, (armed, ready, held));
        var problems = await JudgeAsync(f, row, result, before, done);

        Assert.True(problems.Count == 0, row + "\n" + string.Join("\n", problems));
    }

    private static async Task<Func<RepositorySnapshot, GitWriteRequest>> PrepareAsync(GitFixture template, string row)
    {
        await WriterInterruptionRows.BaseAsync(template);
        return await WriterInterruptionRows.PrepareAsync(template, row);
    }

    // The write nobody stops: it has to end as the row says, leave what every stopped run is compared with, and tell how
    // many Git processes the write starts.
    private static async Task<(RepositoryFingerprint Before, RepositoryFingerprint Done, int Processes)> ReferenceAsync(GitFixture template, string row,
        Func<RepositorySnapshot, GitWriteRequest> build)
    {
        using var f = await CopyAsync(template);
        var before = await RepositoryFingerprint.ReadAsync(f);
        var (result, processes) = await WriteAsync(f, build, null, null);
        Assert.True(result.Outcome == WriterInterruptionRows.Done(row), row + " was not done without a stop: " + result.Outcome + " " + result.Message + " " + result.Details);
        var done = await RepositoryFingerprint.ReadAsync(f);
        var problems = await JudgeAsync(f, row, result, before, done);
        Assert.True(problems.Count == 0, row + " without a stop\n" + string.Join("\n", problems));
        return (before, done, processes);
    }

    // The write as the shell runs it: the workspace opens the repository, the request is built from what it shows, and
    // the write runs through the router, whose own Git processes are counted. The workspace reads with a runner of its
    // own, so its verification is never counted. Cancel comes once the counted process numbered stop has exited, or once
    // the gate armed with what it holds reports that Git waits in it.
    private static async Task<(GitWriteResult Result, int Processes)> WriteAsync(GitFixture f, Func<RepositorySnapshot, GitWriteRequest> build, int? stop,
        (string Armed, string Ready, string Held)? gate)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        var exited = 0;
        var runner = new GitProcessRunner(exited: _ => { if (Interlocked.Increment(ref exited) == stop) cancel.Cancel(); });
        await using var workspace = new RepositoryWorkspace(new CliRepositoryReader(f.Runner), writer: new GitWriteRouter(runner));
        var shown = await workspace.OpenAsync(f.Root, f.Token);
        if (gate is not { } armed) return (await workspace.WriteAsync(build(shown!), cancel.Token), exited);

        await File.WriteAllTextAsync(Path.Combine(f.Root, ".git", armed.Armed), armed.Held, f.Token);
        var pending = workspace.WriteAsync(build(shown!), cancel.Token);
        var ready = Path.Combine(f.Root, ".git", armed.Ready);
        while (!File.Exists(ready) && !pending.IsCompleted) await Task.Delay(20, f.Token);
        // An older Git writes some refs outside a transaction, where the hook never holds them: 2.43 AUTO_MERGE, and 2.45
        // still a symbolic HEAD and the refs git remote renames or removes; 2.50 holds them all. Such a write runs as nobody
        // stopped it.
        if (!File.Exists(ready) && armed.Armed == RefsArmed && SuiteGit.Version < new Version(2, 50)) return (await pending, exited);
        Assert.True(File.Exists(ready), "The gate was never reached: " + (pending.IsCompletedSuccessfully ? pending.Result.Outcome + " " + pending.Result.Message : ""));
        await cancel.CancelAsync();
        return (await pending, exited);
    }

    private static async Task<List<string>> JudgeAsync(GitFixture f, string row, GitWriteResult result, RepositoryFingerprint before, RepositoryFingerprint done)
    {
        var problems = new List<string>();
        void Problem(string text) => problems.Add(Said(result, text));
        var after = await RepositoryFingerprint.ReadAsync(f);
        switch (result.Outcome)
        {
            case GitWriteOutcome.Cancelled:
                if (!WriterInterruptionRows.Canceled(row).Contains(result.Message)) Problem("a sentence this write does not say when it is canceled");
                if (!after.Exact.SequenceEqual(before.Exact)) Problem("the repository changed: " + RepositoryFingerprint.Difference(before.Exact, after.Exact));
                break;
            case GitWriteOutcome.Succeeded or GitWriteOutcome.NeedsResolution:
                if (!after.Content.SequenceEqual(done.Content)) Problem("not what the write nobody stopped left: " + RepositoryFingerprint.Difference(done.Content, after.Content));
                break;
            case GitWriteOutcome.Rejected:
                Problem("Git did not refuse this write");
                break;
        }
        problems.AddRange(await LeftoversAsync(f, result));
        return problems;
    }

    // The repository keeps no lock file, git fsck finds nothing wrong, and a Trag recovery index is there exactly when
    // the outcome names one.
    private static async Task<List<string>> LeftoversAsync(GitFixture f, GitWriteResult result)
    {
        var problems = new List<string>();
        void Problem(string text) => problems.Add(Said(result, text));
        var git = Path.Combine(f.Root, ".git");
        var locks = Directory.EnumerateFiles(git, "*.lock", SearchOption.AllDirectories).Append(Path.Combine(git, "packed-refs.new")).Where(File.Exists)
            .Select(path => Path.GetRelativePath(git, path)).ToArray();
        if (locks.Length != 0) Problem("left " + string.Join(", ", locks));
        var fsck = await f.RunAsync("fsck", "--no-dangling");
        if (fsck.ExitCode != 0) Problem("git fsck: " + Encoding.UTF8.GetString(fsck.StandardOutput) + Encoding.UTF8.GetString(fsck.StandardError));
        var recovery = Directory.EnumerateFiles(git, "trag-write-*.index").Any();
        if (recovery != result.Details.Contains(WriteText.RecoveryIndex(""), StringComparison.Ordinal))
            Problem(recovery ? "a recovery index the outcome does not name" : "names a recovery index that is not there");
        return problems;
    }

    private static string Said(GitWriteResult result, string text) => result.Outcome + " \"" + result.Message + "\": " + text;

    // Each run works on its own copy of the row's repository, whose index is brought up to date for the copy's files.
    // The copy's hooks are the template's.
    private static async Task<GitFixture> CopyAsync(GitFixture template)
    {
        var f = new GitFixture();
        try
        {
            foreach (var folder in Directory.EnumerateDirectories(template.Root, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(f.Root, Path.GetRelativePath(template.Root, folder)));
            foreach (var file in Directory.EnumerateFiles(template.Root, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(f.Root, Path.GetRelativePath(template.Root, file)));
            await f.RunAsync("update-index", "-q", "--refresh");
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    private static async Task HoldRefsAsync(GitFixture template)
    {
        var hooks = Path.Combine(template.Container, "empty-hooks");
        Directory.CreateDirectory(hooks);
        await ShellScript.WriteAsync(Path.Combine(hooks, "reference-transaction"),
            "#!/bin/sh\nupdates=$(cat)\n[ \"$1\" = prepared ] || exit 0\n[ -e .git/" + RefsArmed + " ] || exit 0\nheld=$(cat .git/" + RefsArmed + ")\n"
            + "case \"$updates\" in *\" $held\"*) ;; *) exit 0 ;; esac\nrm -f .git/" + RefsArmed + "\necho ready > .git/" + RefsReady + "\nsleep 30\n", template.Token);
    }
}
