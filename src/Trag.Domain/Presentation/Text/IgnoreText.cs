namespace Trag.Domain.Presentation.Text;

// What ignoring an untracked file says: the line added, a file Git still lists because a later line lets it in, and a
// line that cannot be written.
public static class IgnoreText
{
    // An untracked file's menu ignores it, every file of its kind or its folder, in the repository's .gitignore.
    public const string ThisFile = "This file";
    public static string All(string pattern) => "All " + pattern + " files";
    public static string Folder(string folder) => "Folder " + folder;
    public static string Ignored(string pattern) => "Added " + pattern + " to .gitignore.";
    public const string NotIgnored = "Git still lists the file: .gitignore has the line already, and a later line or a .gitignore in one of its folders lets it in.";
    public static string AddedButListed(string pattern) => "Added " + pattern + " to .gitignore, but Git still lists the file: a .gitignore in one of its folders lets it in.";
    public const string LineInvalid = "A line of .gitignore cannot be empty or hold a line break.";
    public const string GitignoreIsLink = "The repository's .gitignore is a link to another place. Git does not read it, and Trag does not write through it.";
}
