using Trag.Domain.Diff;
using Trag.Domain.Merge;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Rebase;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// The writes the interruption matrix stops: every request GitWriteRouter carries out itself, but the merge tool, which
// waits for a program of the user's, and the submodule update, which belongs to the synchronizer. Each row prepares its
// repository on the shared base and builds its request from the snapshot the shell shows, as the shell does.
internal static class WriterInterruptionRows
{
    // The rows by family: each family's rows are one test class, so the families run side by side.
    private static readonly Dictionary<string, string[]> Families = new()
    {
        ["index"] = ["stage", "unstage", "stage-resolution", "hunk-stage", "hunk-unstage", "hunk-discard", "discard", "commit", "amend", "ignore",
            "file-restore", "file-revert"],
        ["branch"] = ["branch-create", "branch-create-switch", "branch-switch", "branch-checkout", "branch-delete", "branch-rename", "branch-rename-current",
            "tag-create", "tag-delete", "remote-add", "remote-edit", "remote-remove"],
        ["merge"] = ["merge-ff", "merge-commit", "merge-conflict", "merge-abort", "merge-finish", "conflict-mine", "conflict-theirs", "conflict-block", "reopen"],
        ["rebase"] = ["rebase", "rebase-continue", "rebase-reword-continue", "rebase-fold-continue", "rebase-skip", "rebase-abort", "rewrite", "reset-soft",
            "reset-mixed", "reset-hard"],
        ["pick"] = ["cherry-pick", "revert", "revert-conflict", "pick-continue", "pick-abort", "stash-push", "stash-apply", "stash-drop"]
    };

    // The writes whose Git updates a ref, held while it holds the locks of its first transaction, and those whose later
    // transaction updates another ref, held there as well. At the prepared state a symbolic HEAD reads as its branch. A
    // command that commits or moves HEAD deletes AUTO_MERGE in a transaction of its own, which locks packed-refs too; a
    // cherry-pick, a revert and a rebase write and delete the sequencer's own refs as well. Continue amends an edit stop
    // given a new message, and the commit before a fold Git put back, before Git goes on. An amend and a finished rebase
    // copy the notes of the commits they replace under notes.rewriteRef, in a transaction of their own. A rename moves
    // HEAD to the new name after the branch itself.
    private static readonly (string Row, string? Held)[] Refs =
    [
        ("commit", null), ("commit", "AUTO_MERGE"), ("amend", null), ("amend", "refs/notes/commits"), ("merge-finish", null), ("merge-finish", "AUTO_MERGE"),
        ("branch-create", null), ("branch-create-switch", null), ("branch-create-switch", "HEAD"), ("branch-switch", null), ("branch-switch", "AUTO_MERGE"),
        ("branch-checkout", null), ("branch-checkout", "AUTO_MERGE"), ("branch-delete", null), ("branch-rename", null), ("branch-rename-current", null),
        ("branch-rename-current", "HEAD"), ("tag-create", null), ("tag-delete", null), ("remote-edit", null), ("remote-remove", null),
        ("merge-ff", null), ("merge-ff", "AUTO_MERGE"), ("merge-commit", null), ("merge-conflict", null), ("merge-abort", null), ("merge-abort", "refs/heads/main"),
        ("merge-abort", "AUTO_MERGE"),
        ("rebase", null), ("rebase", "refs/heads/side"), ("rebase", "refs/notes/commits"), ("rebase-continue", null), ("rebase-continue", "CHERRY_PICK_HEAD"), ("rebase-reword-continue", null),
        ("rebase-reword-continue", "AUTO_MERGE"), ("rebase-fold-continue", null), ("rebase-fold-continue", "AUTO_MERGE"), ("rebase-skip", null),
        ("rebase-skip", "REBASE_HEAD"), ("rebase-abort", null), ("rebase-abort", "HEAD"),
        ("rewrite", null), ("rewrite", "refs/heads/main"), ("reset-soft", null), ("reset-soft", "AUTO_MERGE"), ("reset-mixed", null), ("reset-mixed", "refs/heads/main"),
        ("reset-hard", null),
        ("cherry-pick", null), ("cherry-pick", "refs/heads/main"), ("revert", null), ("revert", "refs/heads/main"), ("revert", "CHERRY_PICK_HEAD"),
        ("revert-conflict", "REVERT_HEAD"), ("pick-continue", null), ("pick-continue", "AUTO_MERGE"), ("pick-continue", "CHERRY_PICK_HEAD"), ("pick-abort", null),
        ("pick-abort", "refs/heads/main"), ("pick-abort", "AUTO_MERGE"), ("pick-abort", "CHERRY_PICK_HEAD"), ("stash-push", null),
        ("stash-push", "refs/heads/main"), ("stash-push", "ORIG_HEAD"), ("stash-push", "AUTO_MERGE"), ("stash-apply", null), ("stash-drop", null)
    ];

    // The writes whose Git writes z.txt, held while its smudge filter runs.
    private static readonly string[] Tree =
    [
        "hunk-discard", "discard", "file-restore", "file-revert", "branch-create-switch", "branch-switch", "branch-checkout", "merge-ff", "merge-conflict",
        "merge-abort", "conflict-mine", "conflict-theirs", "reopen", "rebase", "rebase-continue", "rebase-skip", "rebase-abort", "reset-hard", "cherry-pick",
        "revert", "pick-abort", "stash-push", "stash-apply"
    ];

    public static TheoryData<string> All(string family) => new(Families[family]);
    public static TheoryData<string> Every() => new(Families.Values.SelectMany(rows => rows));
    public static TheoryData<string, string?> RefLocks(string family)
    {
        var rows = new TheoryData<string, string?>();
        foreach (var (row, held) in Refs.Where(gate => Families[family].Contains(gate.Row))) rows.Add(row, held);
        return rows;
    }
    public static TheoryData<string> WorkingTree(string family) => new(Families[family].Intersect(Tree));

    // The writes Git leaves for the user to finish when nobody stops them.
    public static GitWriteOutcome Done(string row) => row is "merge-conflict" or "rebase-skip" or "revert-conflict" ? GitWriteOutcome.NeedsResolution : GitWriteOutcome.Succeeded;

    // What a write may say when it reports that Cancel stopped it before anything changed: the router's sentence, and the
    // one of the writer that verified it.
    public static string[] Canceled(string row) => [WriteText.Canceled, .. row switch
    {
        "branch-create" or "branch-create-switch" or "branch-switch" or "branch-rename" or "branch-rename-current" => [BranchOutcomeText.Interrupted],
        "branch-checkout" => [BranchOutcomeText.CheckoutInterrupted],
        "branch-delete" => [BranchOutcomeText.DeleteInterrupted("merged")],
        "commit" or "amend" or "merge-finish" => [CommitOutcomeText.Canceled],
        "merge-ff" or "merge-commit" or "merge-conflict" => [MergeOutcomeText.Branch.Stopped],
        "rebase" or "rebase-continue" or "rebase-reword-continue" or "rebase-fold-continue" or "rebase-skip" or "rewrite" => [RebaseText.StoppedBeforeChange],
        "cherry-pick" => [PickText.Interrupted(RepositoryOperation.CherryPick)],
        "revert" or "revert-conflict" => [PickText.Interrupted(RepositoryOperation.Revert)],
        "tag-create" => [TagOutcomeText.Interrupted],
        "tag-delete" => [TagOutcomeText.DeleteInterrupted],
        "reset-soft" or "reset-mixed" or "reset-hard" => [ResetText.Interrupted],
        "remote-add" or "remote-edit" or "remote-remove" => [RemoteText.Interrupted],
        _ => Array.Empty<string>()
    }];

    // main: Base (a.txt, z.txt) then Main, which changes both; other: Base, then Other, which changes both as well, and
    // Other two, which changes z.txt again; side: Base and Side, which adds s.txt; feature: Main and Feature, which
    // changes z.txt; merged: at Main. z.txt goes through the "slow" filter, whose smudge waits once .git/trag-smudge-arm
    // is there; it is configured last, so the setup's own checkouts never wait. One checkout worker writes the files in
    // order.
    public static async Task BaseAsync(GitFixture f)
    {
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.GitAsync("config", "checkout.workers", "1");
        await f.WriteAsync(".gitattributes", "z.txt filter=slow\n");
        await f.WriteAsync("a.txt", "a\n"); await f.WriteAsync("z.txt", "z\n"); await f.CommitAsync("Base");
        await f.GitAsync("branch", "other"); await f.GitAsync("branch", "side");
        await f.WriteAsync("a.txt", "a main\n"); await f.WriteAsync("z.txt", "z main\n"); await f.CommitAsync("Main");
        await f.GitAsync("branch", "merged");
        await f.GitAsync("switch", "-q", "other");
        await f.WriteAsync("a.txt", "a other\n"); await f.WriteAsync("z.txt", "z other\n"); await f.CommitAsync("Other");
        await f.WriteAsync("z.txt", "z other two\n"); await f.CommitAsync("Other two");
        await f.GitAsync("switch", "-q", "side");
        await f.WriteAsync("s.txt", "s\n"); await f.CommitAsync("Side");
        await f.GitAsync("switch", "-q", "-c", "feature", "main");
        await f.WriteAsync("z.txt", "z feature\n"); await f.CommitAsync("Feature");
        await f.GitAsync("switch", "-q", "main");
        await File.WriteAllTextAsync(Path.Combine(f.Root, ".git", "slow-smudge.sh"),
            "if [ -e .git/trag-smudge-arm ]; then rm -f .git/trag-smudge-arm; echo ready > .git/trag-smudge-ready; sleep 30; fi\ncat\n", f.Token);
        await f.GitAsync("config", "filter.slow.smudge", "sh .git/slow-smudge.sh");
        await f.GitAsync("config", "filter.slow.clean", "cat");
    }

    // The row's repository on top of the base, and how its request is built from the snapshot the shell shows.
    public static async Task<Func<RepositorySnapshot, GitWriteRequest>> PrepareAsync(GitFixture f, string row)
    {
        var a = Snapshots.Path("a.txt");
        var z = Snapshots.Path("z.txt");
        var start = await f.GitAsync("rev-parse", "main~1");
        var main = await f.GitAsync("rev-parse", "main");
        switch (row)
        {
            case "stage":
                await f.WriteAsync("a.txt", "a staged\n");
                return shown => new StageRequest(shown, [a]);
            case "unstage":
                await f.WriteAsync("a.txt", "a staged\n"); await f.GitAsync("add", "a.txt");
                return shown => new UnstageRequest(shown, [a]);
            case "stage-resolution":
                await ConflictAsync(f);
                await f.WriteAsync("a.txt", "a resolved\n");
                return shown => new StageRequest(shown, [a], Resolution: true);
            case "hunk-stage" or "hunk-unstage":
                await f.WriteAsync("a.txt", "a hunk\n");
                if (row == "hunk-unstage") await f.GitAsync("add", "a.txt");
                var action = row == "hunk-stage" ? PatchAction.Stage : PatchAction.Unstage;
                var staged = await PatchAsync(f, "a.txt", "+a hunk", action);
                return shown => new PatchRequest(shown, a, staged, action, Lines: false);
            case "hunk-discard":
                await f.WriteAsync("z.txt", "z hunk\n");
                var discarded = await PatchAsync(f, "z.txt", "+z hunk", PatchAction.Discard);
                return shown => new PatchRequest(shown, z, discarded, PatchAction.Discard, Lines: false);
            case "discard":
                await f.WriteAsync("a.txt", "a edited\n"); await f.WriteAsync("z.txt", "z edited\n"); await f.WriteAsync("u.txt", "untracked\n");
                return shown => new DiscardRequest(shown, [a, z, Snapshots.Path("u.txt")]);
            case "commit":
                await f.WriteAsync("a.txt", "a committed\n"); await f.GitAsync("add", "a.txt");
                return shown => new CommitRequest(shown, "Committed");
            case "amend":
                await NoteAsync(f, "main");
                await f.WriteAsync("a.txt", "a committed\n"); await f.GitAsync("add", "a.txt");
                return shown => new CommitRequest(shown, "Main amended", Amend: true);
            case "merge-finish":
                await ConflictAsync(f);
                await f.GitAsync("checkout", "--theirs", "--", "a.txt", "z.txt"); await f.GitAsync("add", "a.txt", "z.txt");
                return shown => new CommitRequest(shown, "Merge branch 'other'", FinishesMerge: true);
            case "branch-create":
                return shown => new BranchRequest(shown, new("topic", Switch: false), BranchAction.Create);
            case "branch-create-switch":
                return shown => new BranchRequest(shown, new("topic", "other"), BranchAction.Create);
            case "branch-switch":
                return shown => new BranchRequest(shown, new("other"), BranchAction.Switch);
            case "branch-checkout":
                var other = await f.GitAsync("rev-parse", "other");
                return shown => new BranchRequest(shown, new(other), BranchAction.Checkout);
            case "branch-delete":
                return shown => new BranchRequest(shown, new("merged"), BranchAction.Delete);
            case "branch-rename":
                return shown => new BranchRenameRequest(shown, "merged", "renamed");
            case "branch-rename-current":
                return shown => new BranchRenameRequest(shown, "main", "renamed");
            case "merge-ff" or "merge-commit" or "merge-conflict":
                var from = row switch { "merge-ff" => "feature", "merge-commit" => "side", _ => "other" };
                var tip = await f.GitAsync("rev-parse", from);
                return shown => new MergeRequest(shown, from, tip, false, "Merge branch '" + from + "'");
            case "merge-abort":
                await ConflictAsync(f);
                return shown => new MergeAbortRequest(shown);
            case "conflict-mine" or "conflict-theirs":
                await ConflictAsync(f);
                var side = row == "conflict-mine" ? ConflictChoice.Mine : ConflictChoice.Theirs;
                return shown => new ConflictSideRequest(shown, z, side);
            case "conflict-block":
                await ConflictAsync(f);
                var conflicted = await f.ReadAsync();
                var text = await new CliFileDiffReader(f.Runner).ReadConflictAsync(f.Root, conflicted.Conflicts.Single(file => file.Path.Display == "a.txt"), f.Token);
                return shown => new ConflictBlockRequest(shown, a, 0, ConflictChoice.Theirs, text!.Hash);
            case "reopen":
                await ConflictAsync(f);
                await f.GitAsync("checkout", "--theirs", "--", "z.txt"); await f.GitAsync("add", "z.txt");
                return shown => new ReopenConflictRequest(shown, z);
            case "rebase":
                await NoteAsync(f, "side");
                await f.GitAsync("switch", "-q", "side");
                return shown => new RebaseRequest(shown, "main", main);
            case "rebase-continue" or "rebase-skip" or "rebase-abort":
                await f.GitAsync("switch", "-q", "other");
                Assert.NotEqual(0, (await f.RunAsync("rebase", "--merge", "main")).ExitCode);
                if (row == "rebase-skip") return shown => new RebaseSkipRequest(shown);
                if (row == "rebase-abort") return shown => new RebaseAbortRequest(shown);
                await f.GitAsync("checkout", "--theirs", "--", "a.txt", "z.txt"); await f.GitAsync("add", "a.txt", "z.txt");
                return shown => new RebaseContinueRequest(shown, MergeMessages.Clean(shown.Rebase!.Message));
            case "rebase-reword-continue":
                await f.GitAsync("switch", "-q", "side");
                await f.GitAsync("-c", "rebase.abbreviateCommands=false", "-c", "sequence.editor=sed -i 1s/^pick/edit/", "rebase", "-i", "main");
                return shown => new RebaseContinueRequest(shown, "Side reworded");
            case "rebase-fold-continue":
                // Fold adds f.txt, so an untracked f.txt makes Git put the fold back after the edit stop at Main; what is
                // staged then is folded into Main.
                await f.WriteAsync("f.txt", "f\n");
                var fold = await f.CommitAsync("Fold");
                var todo = Path.Combine(f.Container, "todo");
                await File.WriteAllTextAsync(todo, "edit " + main + "\nfixup " + fold + "\n", f.Token);
                await f.GitAsync("-c", "sequence.editor=cp '" + todo.Replace('\\', '/') + "'", "rebase", "-i", start);
                await f.WriteAsync("f.txt", "in the way\n");
                Assert.NotEqual(0, (await f.RunAsync("rebase", "--continue")).ExitCode);
                File.Delete(Path.Combine(f.Root, "f.txt"));
                await f.WriteAsync("a.txt", "a folded\n"); await f.GitAsync("add", "a.txt");
                return shown => new RebaseContinueRequest(shown, MergeMessages.Clean(shown.Rebase!.Message));
            case "rewrite":
                return shown => new RewriteRequest(shown, start, [new(main, "Main", "Main", RewriteAction.Reword, "Main reworded")]);
            case "reset-soft" or "reset-mixed" or "reset-hard":
                var mode = row switch { "reset-soft" => ResetMode.Soft, "reset-mixed" => ResetMode.Mixed, _ => ResetMode.Hard };
                return shown => new ResetRequest(shown, start, mode);
            case "cherry-pick":
                var feature = await f.GitAsync("rev-parse", "feature");
                return shown => new CherryPickRequest(shown, feature);
            case "revert" or "revert-conflict":
                // Main again changes z.txt once more, so the revert of Main stops at a conflict there.
                if (row == "revert-conflict") { await f.WriteAsync("z.txt", "z main again\n"); await f.CommitAsync("Main again"); }
                return shown => new RevertRequest(shown, main);
            case "pick-continue" or "pick-abort":
                Assert.NotEqual(0, (await f.RunAsync("cherry-pick", "other")).ExitCode);
                if (row == "pick-abort") return shown => new PickAbortRequest(shown);
                await f.GitAsync("checkout", "--theirs", "--", "z.txt"); await f.GitAsync("add", "z.txt");
                return shown => new PickContinueRequest(shown);
            case "stash-push":
                await f.WriteAsync("a.txt", "a stashed\n"); await f.WriteAsync("z.txt", "z stashed\n");
                return shown => new StashRequest(shown, new("saved"), StashAction.Push);
            case "stash-apply" or "stash-drop":
                await f.WriteAsync("a.txt", "a stashed\n"); await f.WriteAsync("z.txt", "z stashed\n");
                await f.GitAsync("stash", "push", "-m", "saved");
                var stash = await f.GitAsync("rev-parse", "refs/stash");
                var stashAction = row == "stash-apply" ? StashAction.Apply : StashAction.Drop;
                return shown => new StashRequest(shown, new(Id: stash), stashAction);
            case "tag-create":
                return shown => new TagRequest(shown, new("v1", main));
            case "tag-delete":
                await f.GitAsync("tag", "v1");
                return shown => new TagDeleteRequest(shown, "v1");
            case "file-restore":
                return shown => new RestoreFileRequest(shown, start, z);
            case "file-revert":
                return shown => new RevertFileRequest(shown, main, z, null);
            case "ignore":
                await f.WriteAsync("junk.log", "junk\n");
                return shown => new IgnoreRequest(shown, Snapshots.Path("junk.log"), "junk.log");
            case "remote-add":
                return shown => new RemoteAddRequest(shown, "upstream", "https://example.invalid/upstream.git");
            case "remote-edit" or "remote-remove":
                await f.GitAsync("remote", "add", "origin", "https://example.invalid/origin.git");
                await f.GitAsync("update-ref", "refs/remotes/origin/main", main);
                if (row == "remote-remove") await f.GitAsync("branch", "-u", "origin/main", "main");
                return row == "remote-edit"
                    ? shown => new RemoteEditRequest(shown, "origin", "source", "https://example.invalid/source.git")
                    : shown => new RemoteRemoveRequest(shown, "origin");
            default:
                throw new ArgumentOutOfRangeException(nameof(row), row, null);
        }
    }

    // A merge of other into main, stopped at its conflicts in a.txt and z.txt.
    private static async Task ConflictAsync(GitFixture f) => Assert.NotEqual(0, (await f.RunAsync("merge", "--no-edit", "other")).ExitCode);

    // A note on the commit, which Git copies to the commit that replaces it, as notes.rewriteRef tells it to.
    private static async Task NoteAsync(GitFixture f, string commit)
    {
        await f.GitAsync("config", "notes.rewriteRef", "refs/notes/commits");
        await f.GitAsync("notes", "add", "-m", "Reviewed", commit);
    }

    // The patch of the hunk that holds the line, as the diff view builds it.
    private static async Task<string> PatchAsync(GitFixture f, string path, string line, PatchAction action)
    {
        var fromIndex = action == PatchAction.Unstage;
        var file = (await f.ReadAsync()).Files.Single(file => file.Path.Display == path && (fromIndex ? file.IsStaged : file.IsUnstaged));
        var diff = await new CliFileDiffReader(f.Runner).ReadAsync(f.Root, file, fromIndex, false, f.Token);
        var index = diff!.Lines.ToList().FindIndex(candidate => candidate.Text == line);
        return HunkPatch.For(diff.Lines, DiffBlocks.At(diff.Lines, index)!.Value.Lines, file.Path, action);
    }
}
