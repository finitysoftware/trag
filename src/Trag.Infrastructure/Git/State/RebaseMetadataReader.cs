using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Trag.Domain;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Rebase;
using Trag.Domain.Status;

namespace Trag.Infrastructure.Git.State;

// The state of a rebase in progress, read from the files Git's merge backend keeps in rebase-merge: the branch and where it
// goes, the plan done and to do, the step, the commit it stopped at and its message (rebase-merge/message, or MERGE_MSG
// when Git prepared only that). A fingerprint over every one of those files makes a change outside Trag visible while a
// write is in flight. The apply backend, a rebase started on a detached
// HEAD and one with an autostash are shown, but left to the user's Git tools.
internal static class RebaseMetadataReader
{
    private const int Limit = 4 * 1024 * 1024;
    private static readonly string[] Files = ["head-name", "onto", "orig-head", "msgnum", "end", "stopped-sha", "amend", "done", "git-rebase-todo", "message", "autostash", "squash-onto", "current-fixups"];

    public static async Task<RepositoryRebase?> ReadAsync(string gitDirectory, RootRefs roots, CancellationToken ct)
    {
        var directory = Path.Combine(gitDirectory, "rebase-merge");
        // git am keeps its state in rebase-apply too, marked by the applying file; that is no rebase.
        if (!Directory.Exists(directory))
            return Directory.Exists(Path.Combine(gitDirectory, "rebase-apply")) && !File.Exists(Path.Combine(gitDirectory, "rebase-apply", "applying"))
                ? new("", "", "", 0, [], [], null, "", "apply", RebaseText.ApplyBackend) : null;
        var values = new Dictionary<string, string?>();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var name in Files) values[name] = await ReadAsync(Path.Combine(directory, name), name, hash, ct).ConfigureAwait(false);
        // A commit Git replayed but could not make, as one the key could not sign, waits staged with only the message Git
        // prepared for it in MERGE_MSG. A fold that stops on a conflict leaves its message; one Git put back leaves only the
        // folds made so far, and its message is the text Git keeps for the folded commit. Git names the fold there before it
        // tries the commit; a fold skipped at a conflict leaves the file empty until its chain ends. From 2.56 a fold whose
        // commit failed stays done and Git stops to amend it, with its message: no conflict stop writes the amend file.
        var folding = !string.IsNullOrWhiteSpace(values["current-fixups"]);
        var foldPutBack = folding && values["message"] is null;
        var foldNotCommitted = foldPutBack || folding && values["amend"] is not null;
        var message = values["message"] ?? await (foldNotCommitted
            ? ReadAsync(Path.Combine(directory, "message-squash"), "message-squash", hash, ct)
            : ReadAsync(Path.Combine(gitDirectory, "MERGE_MSG"), "MERGE_MSG", hash, ct)).ConfigureAwait(false);
        // REBASE_HEAD is a ref, which a reftable repository keeps in its tables rather than as a file.
        var stoppedAt = roots["REBASE_HEAD"];
        Add("REBASE_HEAD", stoppedAt is null ? null : Encoding.UTF8.GetBytes(stoppedAt), hash);
        var headName = Line(values["head-name"]);
        var (onto, origHead) = (Line(values["onto"]), Line(values["orig-head"]));
        var stopped = GitObjectId.IsValid(Line(stoppedAt)) ? Line(stoppedAt) : GitObjectId.IsValid(Line(values["stopped-sha"])) ? Line(values["stopped-sha"]) : null;
        var branch = headName.StartsWith("refs/heads/", StringComparison.Ordinal) ? headName["refs/heads/".Length..] : "";
        var limitation = branch.Length == 0 ? RebaseText.DetachedStart
            : !GitObjectId.IsValid(onto) || !GitObjectId.IsValid(origHead) ? RebaseText.StateUnreadable
            : values["autostash"] is not null ? RebaseText.RebaseAutostash : "";
        // From the first commit Git goes onto an empty commit of its own, squash-onto, that never joins the history: such a
        // rebase goes onto nothing.
        return new(branch, values["squash-onto"] is null ? onto : "", origHead, Number(values["msgnum"]), RewriteTodo.Parse(values["done"]), RewriteTodo.Parse(values["git-rebase-todo"]),
            stopped, message ?? "", Convert.ToHexString(hash.GetHashAndReset()), limitation)
        {
            StoppedForEdit = values["amend"] is not null && !foldNotCommitted, FoldNotCommitted = foldNotCommitted, FoldPutBack = foldPutBack,
            FoldRepeatsMessage = foldPutBack && RewriteTodo.RepeatsMessage(values["git-rebase-todo"], values["current-fixups"]),
            FoldPlanChanged = foldPutBack && !RewriteTodo.FoldsAgain(values["git-rebase-todo"], values["current-fixups"]),
        };
    }

    // Every file adds whether it exists and its bytes, so a file that appears, changes or goes changes the fingerprint. Git
    // writes the message of a commit with a long one, or the plan of a long rebase, past 4 MB; the text is kept up to there.
    private static async Task<string?> ReadAsync(string path, string name, IncrementalHash hash, CancellationToken ct)
    {
        if (!File.Exists(path)) { Add(name, null, hash); return null; }
        hash.AppendData(Encoding.ASCII.GetBytes(name + "\0")); hash.AppendData([1]);
        var text = await StateFile.ReadAsync(path, hash, Limit, ct).ConfigureAwait(false);
        hash.AppendData([0]);
        return text;
    }

    private static void Add(string name, byte[]? bytes, IncrementalHash hash)
    {
        hash.AppendData(Encoding.ASCII.GetBytes(name + "\0"));
        if (bytes is null) { hash.AppendData([0]); return; }
        hash.AppendData([1]); hash.AppendData(bytes); hash.AppendData([0]);
    }

    private static string Line(string? text) => (text ?? "").Trim();
    private static int Number(string? text) => int.TryParse(Line(text), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
}
