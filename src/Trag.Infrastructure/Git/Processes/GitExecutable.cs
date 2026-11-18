namespace Trag.Infrastructure.Git.Processes;

// The Git program every runner that shares this starts, read at each start: "git" from the PATH until a Git is found or
// chosen, and that one from then on.
public sealed class GitExecutable
{
    public string Path { get; set; } = "git";
}
