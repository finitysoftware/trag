using System.ComponentModel;
using Trag.Application.Ports.Git;
using Trag.Domain.Errors;
using Trag.Domain.Setup;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Setup;

// Git on this machine. The PATH is read as Trag started with it, then as the machine's and the user's stand now, in the
// order Windows gives a new process, so a Git installed while Trag runs is found without a restart, the one a new
// terminal would find; then the standard install folders. Each candidate is asked for
// its version within a limit, since a chosen file can be any program.
public sealed class GitLocator(GitExecutable executable) : IGitInstallations
{
    private static readonly TimeSpan AnswerLimit = TimeSpan.FromSeconds(10);

    public async Task<GitFinding> FindAsync(string? chosen, CancellationToken ct)
    {
        var finding = chosen is null
            ? await GitSearch.FindAsync(GitSearch.Candidates(Paths(), InstallFolders(), File.Exists), path => VersionAsync(path, ct)).ConfigureAwait(false)
            : await GitSearch.ChooseAsync(chosen, path => VersionAsync(path, ct)).ConfigureAwait(false);
        if (finding.Running is { } git) executable.Path = git.Path;
        return finding;
    }

    // The Git's system and global configuration says what it brings.
    public async Task<GitFeatures?> DescribeAsync(GitInstallation git, CancellationToken ct)
    {
        try
        {
            var result = await new GitProcessRunner(executable: new() { Path = git.Path }).RunAsync(Path.GetTempPath(), ["config", "--null", "--show-scope", "--list"], ct).ConfigureAwait(false);
            return result.ExitCode == 0 ? GitSearch.Features(GitSearch.Shared(result.StandardOutput)) : null;
        }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException or FormatException or GitOutputLimitException)
        {
            return null;
        }
    }

    internal static IReadOnlyList<string?> Paths() =>
    [
        Environment.GetEnvironmentVariable("PATH"),
        Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
        Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)
    ];

    private static IEnumerable<string> InstallFolders() => OperatingSystem.IsWindows()
        ? new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }.Select(Environment.GetFolderPath)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"))
            .Where(folder => Path.IsPathFullyQualified(folder)).Select(folder => Path.Combine(folder, "Git", "cmd"))
        : ["/usr/bin", "/usr/local/bin", "/opt/homebrew/bin"];

    // The version the program answers with; null when it does not start, does not answer in time, or answers as no Git.
    private static async Task<Version?> VersionAsync(string path, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(AnswerLimit);
        try
        {
            var result = await new GitProcessRunner(executable: new() { Path = path }).RunAsync(Path.GetTempPath(), ["--version"], limit.Token).ConfigureAwait(false);
            return result.ExitCode == 0 ? GitVersion.Parse(GitBytes.FirstLine(result.StandardOutput)) : null;
        }
        catch (Exception error) when (error is Win32Exception or RepositoryException or IOException or UnauthorizedAccessException or GitOutputLimitException
            || error is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
