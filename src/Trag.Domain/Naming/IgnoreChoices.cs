using System.Text;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Domain.Naming;

// The lines of the repository's .gitignore that would ignore an untracked file: the file itself, every file with its
// extension, or its folder. The file and the folder are anchored at the root, so nothing of the same name elsewhere
// goes with them. Git's pattern characters are escaped, and trailing spaces too, which Git would drop; a line break
// cannot be written into a line, so a name holding one is not offered, and neither is a name that is not its own text.
public static class IgnoreChoices
{
    // Only an untracked file is ignored by a line, and only one whose name gives at least one of them.
    public static bool Offered(FileStatus file) => file.IsUntracked && (File(file.Path) ?? Extension(file.Path) ?? Folder(file.Path)) is not null;

    // A folder Git lists whole, another repository inside this one, is offered as the folder alone.
    public static IgnoreChoice? File(GitPath path) =>
        path.IsDisplayable && !path.Display.EndsWith('/') && Escaped(path.Display) is { } line ? new(path, "/" + line, IgnoreText.ThisFile) : null;

    public static IgnoreChoice? Extension(GitPath path) =>
        path.IsDisplayable && Path.GetExtension(path.Display) is { Length: > 1 } extension && Escaped(extension) is { } line
            ? new(path, "*" + line, IgnoreText.All("*" + extension)) : null;

    public static IgnoreChoice? Folder(GitPath path) =>
        path.IsDisplayable && path.Display.LastIndexOf('/') is > 0 and var end && Escaped(path.Display[..end]) is { } line
            ? new(path, "/" + line + "/", IgnoreText.Folder(path.Display[..(end + 1)])) : null;

    private static string? Escaped(string text)
    {
        if (text.Contains('\n') || text.Contains('\r')) return null;
        var kept = text.TrimEnd(' ');
        var line = new StringBuilder();
        foreach (var character in kept)
        {
            if (character is '\\' or '*' or '?' or '[') line.Append('\\');
            line.Append(character);
        }
        for (var space = kept.Length; space < text.Length; space++) line.Append("\\ ");
        return line.ToString();
    }
}
