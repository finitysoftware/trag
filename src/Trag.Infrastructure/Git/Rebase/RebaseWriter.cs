using System.Globalization;
using System.Text;
using Trag.Application.Ports.Git;
using Trag.Domain;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Rebase;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;
using Trag.Infrastructure.Git.Write;

namespace Trag.Infrastructure.Git.Rebase;

// A rebase started, continued, skipped or aborted from Trag. Each command runs on the real index, because Git keeps a
// rebase going across several commands and its own checks refuse to overwrite local changes. No editor ever opens: a
// stop is committed with the message Trag wrote into Git's rebase state. Every outcome is read back and judged from what
// the repository is afterwards, never from the exit code alone.
internal sealed class RebaseWriter(WriteCommandRunner git, IRepositoryReader reader)
{
    private static readonly IReadOnlyDictionary<string, string?> NoEditor = new Dictionary<string, string?> { ["GIT_EDITOR"] = ":", ["GIT_SEQUENCE_EDITOR"] = ":" };
    private static readonly TimeSpan OperationLimit = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan VerificationLimit = TimeSpan.FromSeconds(30);

    // The merge backend and nothing the user's configuration adds: no autostash, no autosquash, no other branches moved.
    // Git writes the subjects of its plan once, in i18n.logOutputEncoding, so the start asks for UTF-8.
    public async Task<GitWriteResult> StartAsync(RebaseRequest request, RepositorySnapshot fresh, CancellationToken ct)
    {
        var (branch, head) = (fresh.Branch!, fresh.Head!);
        var contained = await git.RunAsync(fresh.Root, ["merge-base", "--is-ancestor", request.OntoHead, head], ct).ConfigureAwait(false);
        if (contained.ExitCode == 0) return new(GitWriteOutcome.Succeeded, RebaseText.UpToDate(branch, request.OntoName));
        if (contained.ExitCode != 1) return GitWriteResult.Rejected(RebaseText.OntoGone);
        return await RunAsync(fresh, ["-c", "i18n.logOutputEncoding=UTF-8", "rebase", "--merge", "--no-rebase-merges", "--no-autostash", "--no-autosquash", "--no-update-refs", "--no-rerere-autoupdate", request.OntoHead],
            new(branch, request.OntoHead, head, head, null), request.OntoName, RebaseText.NotStarted, ct).ConfigureAwait(false);
    }

    // The plan has to list exactly the commits after the base, since Git drops one left out without a word; with no base,
    // from the first commit, that is the whole history and Git starts it anew from the root. The messages and the plan wait
    // in trag-rebase for Git: the plan replaces Git's own, the messages are read by its exec lines, so they stay while the
    // rewrite is stopped and go once it ends. An exec line that fails, as an amend Git cannot sign does, runs again on
    // Continue and on Skip instead of being passed over with its message.
    public async Task<GitWriteResult> RewriteAsync(RewriteRequest request, RepositorySnapshot fresh, CancellationToken ct)
    {
        var (branch, head) = (fresh.Branch!, fresh.Head!);
        var listed = await git.RunAsync(fresh.Root, ["rev-list", request.Base is { } start ? start + "..HEAD" : "HEAD", "--"], ct).ConfigureAwait(false);
        var commits = Encoding.UTF8.GetString(listed.StandardOutput).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (listed.ExitCode != 0 || !commits.ToHashSet().SetEquals(request.Steps.Select(step => step.Id))) return GitWriteResult.Rejected(RewriteText.PlanStale);

        var folder = PlanFolder(fresh);
        Directory.CreateDirectory(folder);
        var script = RewriteTodo.For(request.Steps, folder);
        var encoding = new UTF8Encoding(false);
        // A message ends with one line break, as Continue gives a stop's, however many the editor left at its end.
        for (var index = 0; index < script.Messages.Count; index++)
            await File.WriteAllTextAsync(Path.Combine(folder, (index + 1).ToString(CultureInfo.InvariantCulture) + ".txt"), script.Messages[index].TrimEnd() + "\n", encoding, ct).ConfigureAwait(false);
        var todo = Path.Combine(folder, "todo");
        await File.WriteAllTextAsync(todo, script.Todo, encoding, ct).ConfigureAwait(false);

        var editors = new Dictionary<string, string?>(NoEditor) { ["GIT_SEQUENCE_EDITOR"] = RewriteTodo.Editor(todo) };
        var onto = request.Base ?? "";
        var result = await CleaningUpAsync(fresh, RunAsync(fresh, ["rebase", "--interactive", "--reschedule-failed-exec", "--no-autostash", "--no-autosquash", "--no-update-refs", "--no-rerere-autoupdate", request.Base ?? "--root"],
            new(branch, onto, head, head, null), GitObjectId.Short(onto), RebaseText.NotStarted, ct, editors)).ConfigureAwait(false);
        return result.Outcome == GitWriteOutcome.Succeeded ? result with { Message = RewriteText.Rewritten(branch) } : result;
    }

    public async Task<GitWriteResult> ContinueAsync(RebaseContinueRequest request, RepositorySnapshot fresh, CancellationToken ct)
    {
        // Git could not commit a fold and left its changes staged: committed on Continue they would be a commit of their own.
        // They are folded into the commit before, as Git advises, with what else is staged; the amend runs the user's hooks
        // as every Trag commit does. A fold Git put back then finds nothing left to fold. One Git stopped to amend, from
        // 2.56, takes the message Git prepared for the fold, which the last fold of a chain gets from nowhere else; Git
        // reads its own file, in the commit encoding it wrote it in. A plan changed so that it no longer folds a commit put
        // back first is the user's own, which Git refuses to go on with beside staged changes.
        if (fresh.Rebase!.FoldNotCommitted)
        {
            if (fresh.Rebase.FoldPlanChanged) return GitWriteResult.Rejected(RebaseText.FoldPlanChanged);
            if (fresh.Rebase.FoldRepeatsMessage) return GitWriteResult.Rejected(RebaseText.FoldRepeatsMessage);
            // Git refuses to go on beside changes that are not staged, but only after the fold has moved the branch.
            if (HasChangesNotStaged(fresh)) return GitWriteResult.Rejected(RebaseText.FoldNeedsStaged);
            // A fold an untracked file stood in the way of left nothing staged; Git then tries it again as it is.
            if (!fresh.Rebase.FoldPutBack || fresh.Files.Any(file => file.IsStaged))
            {
                var folded = await AmendAsync(fresh, fresh.Rebase.FoldPutBack ? ["--no-edit"]
                    : ["--cleanup=strip", "--file=" + Path.Combine(fresh.GitDirectory!, "rebase-merge", "message")], null, ct).ConfigureAwait(false);
                var details = GitOutputText.Details(folded);
                if (folded.ExitCode != 0)
                    return GitWriteResult.Rejected(CommitSignings.Refused(fresh.Signing, details) ? RebaseText.NotSigned(fresh.Signing) : RebaseText.NotContinued, details);
            }
            return await CleaningUpAsync(fresh, RunAsync(fresh, ["rebase", "--continue"], RebaseStateCheck.Of(fresh), fresh.Rebase.OntoText, RebaseText.NotContinued, ct)).ConfigureAwait(false);
        }
        var message = request.Message.TrimEnd() + "\n";
        // An edit stop's commit is made already, so a message changed there is given to it, with whatever is staged,
        // before Git goes on; the amend runs the user's hooks as every Trag commit does. One they refuse leaves the stop's
        // message as it was, so Continue tried again amends again. Git goes on only once every change is staged, and never
        // beside a change not staged once the commit it stopped at has moved, so that is refused before the amend, and
        // after it, with the stop's message kept, when the amend left such a change, as a hook may. Git keeps no amend
        // file for a reword it replayed, and would commit what is staged there as a commit of its own: that is amended too.
        if (fresh.Rebase!.StoppedForEdit && HasChangesNotStaged(fresh)) return GitWriteResult.Rejected(RebaseText.EditNeedsStaged);
        if (fresh.Rebase.Rewords(request.Message)
            || fresh.Rebase.StoppedForEdit && fresh.Files.Any(file => file.IsStaged) && !File.Exists(Path.Combine(fresh.GitDirectory!, "rebase-merge", "amend")))
        {
            var amended = await AmendAsync(fresh, ["--allow-empty", "--cleanup=verbatim", "--file=-"], Encoding.UTF8.GetBytes(MessageLines.ForGit(message)), ct)
                .ConfigureAwait(false);
            if (amended.ExitCode != 0) return GitWriteResult.Rejected(RebaseText.NotContinued, GitOutputText.Details(amended));
            if ((await git.RunAsync(fresh.Root, ["diff", "--quiet", "--ignore-submodules"], ct).ConfigureAwait(false)).ExitCode == 1)
                return GitWriteResult.Rejected(RebaseText.EditChangedWhileAmended);
        }
        // Git commits the stop with the message it keeps for it; the rebase view's editor is that message. It is written
        // whole, since a cancel halfway would leave Git a message cut short.
        await File.WriteAllTextAsync(Path.Combine(fresh.GitDirectory!, "rebase-merge", "message"), message, new UTF8Encoding(false), CancellationToken.None).ConfigureAwait(false);
        // Git takes the message through its editor here and adds its own help as comments; the comment character is one
        // no line of the message starts with, so stripping drops Git's lines alone, also under commit.cleanup=verbatim.
        return await CleaningUpAsync(fresh, RunAsync(fresh, ["-c", "core.commentChar=" + MessageLines.CommentChar(message), "-c", "commit.cleanup=strip", "rebase", "--continue"],
            RebaseStateCheck.Of(fresh), fresh.Rebase!.OntoText,
            RebaseText.NotContinued, ct)).ConfigureAwait(false);
    }

    // Git's skip of a fold it put back resets its changes and folds it again from the plan, so nothing is left out. A fold
    // Git stopped to amend it skips as any commit: its changes are left out.
    public Task<GitWriteResult> SkipAsync(RepositorySnapshot fresh, CancellationToken ct) => fresh.Rebase!.FoldPutBack
        ? Task.FromResult(GitWriteResult.Rejected(fresh.Rebase.FoldPlanChanged ? RebaseText.FoldPlanChanged
            : fresh.Rebase.FoldRepeatsMessage ? RebaseText.FoldRepeatsMessage : RebaseText.FoldNotSkipped))
        : CleaningUpAsync(fresh, RunAsync(fresh, ["rebase", "--skip"], RebaseStateCheck.Of(fresh), fresh.Rebase.OntoText, RebaseText.NotSkipped, ct));

    // Abort is answered by the branch being back at the commit the rebase started from.
    public Task<GitWriteResult> AbortAsync(RepositorySnapshot fresh, CancellationToken ct)
    {
        var check = RebaseStateCheck.Of(fresh);
        return CleaningUpAsync(fresh, VerifiedGitOperation.RunAsync(
            token => git.RunAsync(fresh.Root, ["rebase", "--abort"], token, variables: NoEditor),
            async (command, _, token) => check.IsAborted(await reader.ReadAsync(fresh.Root, token).ConfigureAwait(false))
                ? new(GitWriteOutcome.Succeeded, RebaseText.Aborted(check.Branch), GitOutputText.Details(command))
                : new(GitWriteOutcome.Uncertain, RebaseText.AbortNotVerified, GitOutputText.Details(command)),
            Unverified, OperationLimit, VerificationLimit, ct, Left(fresh)));
    }

    // The amend Continue runs itself before Git goes on writes HEAD and deletes AUTO_MERGE as Git's own commands do,
    // so Cancel takes back the same locks.
    private async Task<GitResult> AmendAsync(RepositorySnapshot fresh, string[] args, byte[]? input, CancellationToken ct)
    {
        var left = Left(fresh);
        try { return await git.RunAsync(fresh.Root, ["commit", "--amend", .. args], ct, input: input, literalPathspecs: false, variables: NoEditor).ConfigureAwait(false); }
        finally { left.Release(ct); }
    }

    // The refs a rebase command writes: HEAD, the rebased branch, ORIG_HEAD, AUTO_MERGE, the sequencer's REBASE_HEAD and
    // CHERRY_PICK_HEAD, and packed-refs, which Git locks as well to delete one.
    private static StoppedWriteLock Left(RepositorySnapshot fresh) => new(StoppedWriteLock.HeadStorage(fresh, "ORIG_HEAD", "REBASE_HEAD", "CHERRY_PICK_HEAD"));

    // A rewrite's plan and messages stay while Git's rebase runs, since its exec lines read them, and go once it is over,
    // however it ended. A file another program holds open stays behind harmlessly: the next rewrite writes its plan over it.
    private static async Task<GitWriteResult> CleaningUpAsync(RepositorySnapshot fresh, Task<GitWriteResult> run)
    {
        try { return await run.ConfigureAwait(false); }
        finally
        {
            var folder = PlanFolder(fresh);
            try { if (Directory.Exists(folder) && !Directory.Exists(Path.Combine(fresh.GitDirectory!, "rebase-merge"))) Directory.Delete(folder, true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string PlanFolder(RepositorySnapshot fresh) => Path.Combine(fresh.GitDirectory!, "trag-rebase");

    // Git's own check before it goes on: tracked changes that are not staged, with submodules left out.
    private static bool HasChangesNotStaged(RepositorySnapshot fresh) => fresh.Files.Any(file => file.IsWorkingTreeChange && !file.IsUntracked && !file.IsSubmodule);

    private Task<GitWriteResult> RunAsync(RepositorySnapshot fresh, string[] args, RebaseStateCheck check, string ontoName, string refused, CancellationToken ct,
        IReadOnlyDictionary<string, string?>? editors = null) =>
        VerifiedGitOperation.RunAsync(
            token => git.RunAsync(fresh.Root, args, token, literalPathspecs: false, variables: editors ?? NoEditor),
            async (command, failure, token) =>
            {
                var after = await reader.ReadAsync(fresh.Root, token).ConfigureAwait(false);
                var onOnto = after.Head is { } head && (check.Onto.Length == 0
                    || (await git.RunAsync(fresh.Root, ["merge-base", "--is-ancestor", check.Onto, head], token).ConfigureAwait(false)).ExitCode == 0);
                return check.Judge(after, onOnto, GitOutputText.Details(command), failure is not null, ontoName, refused);
            },
            Unverified, OperationTimeLimit.Of(fresh, OperationLimit), VerificationLimit, ct, Left(fresh));

    private static GitWriteResult Unverified(GitResult? command, Exception _) =>
        new(GitWriteOutcome.Uncertain, RebaseText.OutcomeNotVerified,
            GitOutputText.Details(command));
}
