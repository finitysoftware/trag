using Trag.Domain.Naming;
using Trag.Domain.Setup;
using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.Git.Setup;

// Where Trag looks for Git and which one it takes, apart from the machine and the programs that answer.
internal static class GitSearch
{
    public static string FileName => OperatingSystem.IsWindows() ? "git.exe" : "git";

    // The folders of each PATH in order, then the install folders: each folder once, and only where the program is, looked
    // in only when the search gets that far. A folder that is not a whole path would be looked in from wherever Trag was
    // started, so it is passed by.
    public static IEnumerable<(string Path, GitOrigin Origin)> Candidates(IEnumerable<string?> paths, IEnumerable<string> installFolders, Func<string, bool> exists) =>
        paths.SelectMany(path => (path ?? "").Split(System.IO.Path.PathSeparator)).Select(folder => (Folder: folder.Trim().Trim('"'), Origin: GitOrigin.Path))
            .Concat(installFolders.Select(folder => (Folder: folder, Origin: GitOrigin.InstallFolder)))
            .Where(candidate => System.IO.Path.IsPathFullyQualified(candidate.Folder))
            .Select(candidate => (Path: System.IO.Path.Combine(candidate.Folder, FileName), candidate.Origin))
            .DistinctBy(candidate => candidate.Path)
            .Where(candidate => exists(candidate.Path));

    // The first candidate that answers with a version new enough; without one the first older one, as too old. Each
    // candidate is found away from the caller, since a folder of the PATH can be a slow or offline network drive.
    public static async Task<GitFinding> FindAsync(IEnumerable<(string Path, GitOrigin Origin)> candidates, Func<string, Task<Version?>> version)
    {
        GitInstallation? old = null;
        using var each = candidates.GetEnumerator();
        while (await Task.Run(each.MoveNext).ConfigureAwait(false))
        {
            var (path, origin) = each.Current;
            if (await version(path).ConfigureAwait(false) is not { } answer) continue;
            var git = new GitInstallation(path, answer, origin);
            if (answer >= GitLimits.MinimumGit) return new(GitProblem.None, git);
            old ??= git;
        }
        return old is null ? new(GitProblem.Missing) : new(GitProblem.TooOld, old);
    }

    // A chosen file is asked only when it is named as Git, since asking runs it.
    public static async Task<GitFinding> ChooseAsync(string path, Func<string, Task<Version?>> version) =>
        !string.Equals(System.IO.Path.GetFileName(path), FileName, StringComparison.OrdinalIgnoreCase) || await version(path).ConfigureAwait(false) is not { } answer
            ? new(GitProblem.NotGit)
            : new(answer >= GitLimits.MinimumGit ? GitProblem.None : GitProblem.TooOld, new(path, answer, GitOrigin.Chosen));

    // The system's and the user's entries of "config --null --show-scope --list", whose records come in pairs, the scope
    // first: what Git brings for every repository, without the repository it was asked from or its own command line.
    public static GitConfiguration Shared(byte[] listed) =>
        new([.. GitConfigParser.Parse(listed).Chunk(2).Where(pair => pair is [{ Key: "system" or "global" }, _]).Select(pair => pair[1])]);

    // What a Git brings by its configuration: Git LFS when its filter is set, the credential helper Git asks last (an empty
    // one clears those before it), and how it signs commits.
    public static GitFeatures Features(GitConfiguration config) =>
        new(config.LfsInstalled, config.Value("credential.helper") is { Length: > 0 } helper ? helper : null, config.Signing);
}
