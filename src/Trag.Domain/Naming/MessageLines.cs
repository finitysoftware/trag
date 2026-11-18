namespace Trag.Domain.Naming;

// A message as Git keeps it: Trag's editors end a typed line with Windows' line break, and Git, told to keep a message
// verbatim, would keep a carriage return before every line feed.
public static class MessageLines
{
    public static string ForGit(string message) => message.Replace("\r\n", "\n", StringComparison.Ordinal);

    // Git commits a message it was told to edit, as a continued rebase or pick is, without every line that starts with
    // its comment character. This one is the first of the candidates Git's own choice goes through that no line of the
    // message starts with, so each line the message has stays.
    public static string CommentChar(string message)
    {
        var starts = message.Split('\n').Where(line => line.Length != 0).Select(line => line[0]).ToHashSet();
        return "#;@!$%^&|:".First(candidate => !starts.Contains(candidate)).ToString();
    }
}
