using System.Text;
using Trag.Domain.Naming;

namespace Trag.Domain.Rebase;

// A rewrite's plan in Git's words: one line per commit in the plan's order, with the full id and the subject as a comment.
// A squash or a fixup is a fixup, so no editor opens for a joined message; a drop is said, because Git may be set to refuse
// a commit left out. After the last commit of a group whose message is set, an exec line amends it from a file, which
// holds through a stop on a conflict too, and is committed verbatim, as every message Trag commits. The exec line names
// the file's UTF-8 itself, since a rewrite stopped in Trag may be continued in a terminal, whose Git has none of the
// settings Trag gives its own. The folder goes to sh: slashes only, single-quoted.
public static class RewriteTodo
{
    // Git's plan read back: "pick <id> # <subject>" in current Git, "pick <id> <subject>" in older ones; fixup may carry
    // an option first. A commit without a message is "pick <id> #", as Git saves its line without the trailing space.
    // Commands without a commit (exec, break, label, reset, update-ref) are not commits to show.
    public static IReadOnlyList<RebaseCommit> Parse(string? text) => [.. Commits(text).Select(line => new RebaseCommit(line.Action, line.Id, line.Subject))];

    // Git folds a commit it put back again from the plan's first line that names a commit to replay, after the exec, break,
    // label and drop lines a plan edited in a terminal may put before it. A squash adds its message to the fold's a second
    // time, and so does a fixup -C or -c once a squash is in the fold; current-fixups lists the folds so far by command
    // alone, while the plan has "s", "f" or "d" once it is edited under rebase.abbreviateCommands.
    public static bool RepeatsMessage(string? todo, string? fixups) =>
        FirstReplayed(todo) is ("squash" or "s", _, _, _) or ("fixup" or "f", "-C" or "-c", _, _)
        && (fixups ?? "").Split('\n').Any(line => line.StartsWith("squash ", StringComparison.Ordinal));

    // The fold Git put back is the commit the last line of current-fixups names in full, as Git writes it there before it
    // tries the commit. A plan changed in a terminal (git rebase --edit-todo) may no longer fold that commit first.
    public static bool FoldsAgain(string? todo, string? fixups)
    {
        var putBack = (fixups ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault()?.Split(' ')[^1] ?? "";
        return FirstReplayed(todo) is ("squash" or "s" or "fixup" or "f", _, var id, _) && putBack.StartsWith(id, StringComparison.Ordinal);
    }

    private static (string Action, string Option, string Id, string Subject) FirstReplayed(string? todo) =>
        Commits(todo).FirstOrDefault(line => line.Action is not ("drop" or "d"));

    // Each line of the plan that names a commit, with the option a fixup may carry before the commit.
    private static IEnumerable<(string Action, string Option, string Id, string Subject)> Commits(string? text)
    {
        foreach (var raw in (text ?? "").Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            var at = parts.Length > 2 && parts[1].StartsWith('-') ? 2 : 1;
            // An option comes before the commit, so the commit is the third word and the subject what follows it.
            if (at == 2) parts = line.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= at || !GitObjectId.IsAbbreviation(parts[at])) continue;
            var subject = parts.Length > at + 1 ? parts[at + 1] : "";
            yield return (parts[0], at == 2 ? parts[1] : "", parts[at], subject == "#" ? "" : subject.StartsWith("# ", StringComparison.Ordinal) ? subject[2..] : subject);
        }
    }

    public static RewriteScript For(IReadOnlyList<RewriteStep> steps, string messageDirectory)
    {
        var todo = new StringBuilder();
        var messages = new List<string>();
        var folder = messageDirectory.Replace('\\', '/');
        var group = new List<RewriteStep>();
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var command = step.Action switch { RewriteAction.Drop => "drop", _ when step.IsFolded => "fixup", _ => "pick" };
            todo.Append(command).Append(' ').Append(step.Id).Append(" # ").Append(OneLine(step.Subject)).Append('\n');
            if (step.Action == RewriteAction.Drop) continue;
            if (!step.IsFolded) group = [];
            group.Add(step);
            if (steps.Skip(index + 1).FirstOrDefault(next => next.Action != RewriteAction.Drop) is { IsFolded: true }) continue;
            if (RewritePlan.SetsMessage(group))
            {
                messages.Add(MessageLines.ForGit(group[0].Message));
                todo.Append("exec git -c i18n.commitEncoding=UTF-8 commit --amend --allow-empty --no-verify --cleanup=verbatim -F ").Append(Quoted(folder + "/" + messages.Count + ".txt")).Append('\n');
            }
        }
        return new(todo.ToString(), messages);
    }

    // The sequence editor Git runs on its plan: Trag's plan copied over it.
    public static string Editor(string todoPath) => "cp " + Quoted(todoPath.Replace('\\', '/'));

    // A subject stays a comment on its commit's line, whatever it holds.
    private static string OneLine(string subject) => new([.. subject.Select(character => char.IsControl(character) ? ' ' : character)]);

    private static string Quoted(string path) => "'" + path.Replace("'", "'\\''") + "'";
}
