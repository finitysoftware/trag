using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;

namespace Trag.Domain.Blame;

// The rows of a blamed file. A block is the lines next to each other that one commit last changed; a commit's age is its
// rank by time among the file's commits, spread from 0 to Newest, and a file of one commit is all newest. The lines are
// colored as the diff colors them, by the file's extension.
public static class BlameRows
{
    public const int Newest = 4;

    public static IReadOnlyList<BlameRow> Of(FileBlame blame, string path, DateTimeOffset now)
    {
        var times = blame.Lines.Select(line => line.Commit.Time).Distinct().Order().ToArray();
        var ages = times.Select((time, rank) => (time, age: times.Length == 1 ? Newest : (int)Math.Round(Newest * rank / (times.Length - 1.0))))
            .ToDictionary(pair => pair.time, pair => pair.age);
        var tokens = Highlight(blame, SyntaxScanner.LanguageOf(path));
        return [.. blame.Lines.Select((line, index) =>
        {
            var starts = index == 0 || blame.Lines[index - 1].Commit.Id != line.Commit.Id;
            return new BlameRow(line, starts, ages[line.Commit.Time], starts ? BlameText.Label(line.Commit, now) : "") { Tokens = tokens[index] };
        })];
    }

    // The whole file is scanned from its first line, so a comment that opens on one line colors the next ones; past the
    // budget the rest stays plain.
    private static SyntaxToken[][] Highlight(FileBlame blame, SyntaxLanguage language)
    {
        var tokens = new SyntaxToken[blame.Lines.Count][];
        Array.Fill(tokens, []);
        if (language == SyntaxLanguage.Plain) return tokens;
        var scanner = new SyntaxScanner(language, prefix: 0);
        var budget = new SyntaxBudget();
        for (var index = 0; index < blame.Lines.Count; index++)
        {
            var text = blame.Lines[index].Text;
            if (!budget.Covers(text.Length)) break;
            tokens[index] = scanner.Scan(text);
            budget.Spend(text.Length, tokens[index].Length);
        }
        return tokens;
    }
}
