using System.Globalization;
using Trag.Domain.Rebase;
using Trag.TestSupport;
using Xunit.Sdk;

namespace Trag.Domain.UnitTests.Rebase;

// Plans a rewrite accepts, generated from fixed seeds: one to twelve commits, each picked, reworded, squashed, fixed up or
// dropped, with subjects and messages that hold #, ;, blank lines and CRLF. The todo Trag hands Git for such a plan reads
// back as the same commits with their commands and subjects, and replayed as Git replays it gives each commit of the
// result the message its group is meant to get. A failure names its seed; an InlineData of its own with a count of one
// replays it.
public sealed class RewriteTodoPropertyTests
{
    private const string Directory = "D:/Repos/my repo/.git/trag-rebase";
    private const string AmendFrom = "exec git -c i18n.commitEncoding=UTF-8 commit --amend --allow-empty --no-verify --cleanup=verbatim -F '" + Directory + "/";

    // Subjects as %s gives them: one line, nothing after its last visible character.
    private static readonly string[] Subjects =
        ["", "Add login", "#123 Fix", "# Not a comment", "#", "; Starts with a semicolon", "  Indented", "exec rm -rf .", "pick 1234567 # Other", "fixup -C 1234567", "Dodaj „ćevape“ 🙂"];

    private static readonly string[] Lines = ["Body", "", "# hash line", "#", "; semicolon line", ";", "  indented", "Signed-off-by: Ana <ana@example.com>"];

    [Theory]
    [InlineData(0, 1000)]
    public void WhenARandomPlanIsWrittenAndReadBack_ShouldListEveryCommitInOrderWithItsCommandAndSubject(int first, int count) =>
        Assert.All(Enumerable.Range(first, count), seed =>
        {
            var steps = Plan(seed);
            Assert.Equal(steps.Select(step => (Command(step.Action), step.Id, step.Subject)),
                RewriteTodo.Parse(RewriteTodo.For(steps, Directory).Todo).Select(commit => (commit.Action, commit.Id, commit.Subject)));
        });

    // Each commit of the result holds its group's commits and the message the group's first commit carries in the plan,
    // with line feeds alone, as Git keeps a message.
    [Theory]
    [InlineData(0, 1000)]
    public void WhenARandomPlanIsReplayedAsGitWould_ShouldGiveEachResultingCommitItsGroupsMessageWithLineFeeds(int first, int count) =>
        Assert.All(Enumerable.Range(first, count), seed =>
        {
            var steps = Plan(seed);
            Assert.Equal(Intended(steps), Replayed(RewriteTodo.For(steps, Directory), steps));
        });

    private static string Command(RewriteAction action) =>
        action switch { RewriteAction.Drop => "drop", RewriteAction.Squash or RewriteAction.Fixup => "fixup", _ => "pick" };

    // The first plan the seed's generator gives that the rewrite accepts: distinct commits, each with any action and
    // subject, an original message with LF, and a message that is the original or a new one with CRLF or LF, sometimes
    // ending with a line break.
    private static RewriteStep[] Plan(int seed)
    {
        var random = new Random(seed);
        while (true)
        {
            var ids = "0123456789abcdef".ToCharArray();
            random.Shuffle(ids);
            var steps = ids[..random.Next(1, 13)].Select(id =>
            {
                var original = Message(random, "\n");
                var lineBreak = random.Next(2) == 0 ? "\r\n" : "\n";
                var message = random.Next(2) == 0 ? original : Message(random, lineBreak) + (random.Next(4) == 0 ? lineBreak : "");
                return new RewriteStep(Snapshots.Id(id), Subjects[random.Next(Subjects.Length)], original, (RewriteAction)random.Next(5), message);
            }).ToArray();
            if (RewritePlan.Refusal(steps) is null) return steps;
        }
    }

    private static string Message(Random random, string lineBreak) =>
        string.Join(lineBreak, Enumerable.Range(0, random.Next(1, 5)).Select(_ => Lines[random.Next(Lines.Length)]));

    // The commits the plan means: a commit kept and not folded starts one with its message, a squashed or fixed-up one
    // joins the commit before it, and a dropped one is left out.
    private static IReadOnlyList<(string Ids, string Message)> Intended(IReadOnlyList<RewriteStep> steps)
    {
        var commits = new List<(List<string> Ids, string Message)>();
        foreach (var step in steps.Where(step => step.Action != RewriteAction.Drop))
            if (step.IsFolded) commits[^1].Ids.Add(step.Id);
            else commits.Add(([step.Id], step.Message.Replace("\r\n", "\n", StringComparison.Ordinal)));
        return [.. commits.Select(commit => (string.Join(",", commit.Ids), commit.Message))];
    }

    // The todo replayed as Git replays it: a pick makes a commit with the picked commit's message, a fixup joins the
    // commit before it and keeps that one's message, a drop leaves its commit out, and an exec line amends the last commit
    // with the message from its file. Any other line is none this plan should hold, and fails the test.
    private static IReadOnlyList<(string Ids, string Message)> Replayed(RewriteScript script, IReadOnlyList<RewriteStep> steps)
    {
        var commits = new List<(List<string> Ids, string Message)>();
        foreach (var line in script.Todo.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var words = line.Split(' ');
            if (words[0] == "pick") commits.Add(([words[1]], steps.Single(step => step.Id == words[1]).Original));
            else if (words[0] == "fixup") commits[^1].Ids.Add(words[1]);
            else if (line.StartsWith(AmendFrom, StringComparison.Ordinal) && line.EndsWith(".txt'", StringComparison.Ordinal))
                commits[^1] = (commits[^1].Ids, script.Messages[int.Parse(line[AmendFrom.Length..^".txt'".Length], CultureInfo.InvariantCulture) - 1]);
            else if (words[0] != "drop") throw new XunitException("Git would not replay " + line);
        }
        return [.. commits.Select(commit => (string.Join(",", commit.Ids), commit.Message))];
    }
}
