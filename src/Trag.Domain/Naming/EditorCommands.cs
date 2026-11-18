namespace Trag.Domain.Naming;

// What an editor's command line says about it: a terminal editor started from Trag would never come back, since there is
// no terminal to type in. The program is a quoted path up to its closing quote, since it may hold spaces, or else the
// command up to its first space.
public static class EditorCommands
{
    private static readonly string[] Terminal = ["vi", "vim", "nvim", "nano", "emacs", "pico", "less", "more"];

    public static bool NeedsTerminal(string command)
    {
        var program = Program(command.Trim()).Replace('\\', '/').Split('/')[^1];
        return Terminal.Contains(Path.GetFileNameWithoutExtension(program), StringComparer.OrdinalIgnoreCase);
    }

    private static string Program(string command) =>
        command is [var quote and ('"' or '\''), .. var rest] && rest.IndexOf(quote) is var end and >= 0 ? rest[..end] : command.Split(' ', 2)[0];
}
