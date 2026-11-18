using Trag.Domain.Naming;

namespace Trag.Domain.Paths;

// A file of the repository as the machine has it: its full path in the working tree, and whether opening it hands it to
// a program that shows it. A file that would run is not opened, since a repository may be someone else's.
public static class WorkingFile
{
    public static string PathIn(string root, GitPath path) => Path.GetFullPath(Path.Combine(root, path.Display));

    public static bool Opens(GitPath path) => !FileNames.Runs(path);
}
