namespace Trag.Infrastructure.Git.Diagnostics;

// Git says what went wrong in its own words. Each caller keeps its own table, because the sentence Trag answers with
// belongs to that operation; only the reading is shared. The first row whose phrases appear decides, so the order of
// a table is part of its meaning: put the narrow cause above the broad one.
internal static class GitFailureText
{
    public static string Classify(string details, (string[] Phrases, string Message)[] table, string fallback)
    {
        foreach (var (phrases, message) in table)
            if (Array.Exists(phrases, phrase => details.Contains(phrase, StringComparison.OrdinalIgnoreCase))) return message;
        return fallback;
    }
}
