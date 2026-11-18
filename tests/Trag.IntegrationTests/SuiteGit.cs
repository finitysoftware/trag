using System.Globalization;
using System.Text;
using Trag.Infrastructure.Git.Processes;

namespace Trag.IntegrationTests;

// The Git the suite runs: the first git on PATH, as every runner starts it. Trag works with Git 2.43 or newer, but some
// of what the tests hold Git does only from a later version on.
internal static class SuiteGit
{
    public static Version Version { get; } = Read();

    public static string? Skip(int major, int minor) =>
        Version < new Version(major, minor) ? "Git " + major + "." + minor + " or newer does this; the suite runs Git " + Version + "." : null;

    public static string? SkipFrom(int major, int minor) =>
        Version >= new Version(major, minor) ? "Git before " + major + "." + minor + " does this; the suite runs Git " + Version + "." : null;

    private static Version Read()
    {
        var output = new GitProcessRunner().RunAsync(Path.GetTempPath(), ["--version"], CancellationToken.None).GetAwaiter().GetResult();
        var number = Encoding.ASCII.GetString(output.StandardOutput).Split(' ')[2].Split('.');
        return new(int.Parse(number[0], CultureInfo.InvariantCulture), int.Parse(number[1], CultureInfo.InvariantCulture));
    }
}

// A test of what Git does from the given version on, skipped under an older one.
internal sealed class GitFactAttribute : FactAttribute
{
    public GitFactAttribute(int major, int minor) => Skip = SuiteGit.Skip(major, minor);
}

internal sealed class GitTheoryAttribute : TheoryAttribute
{
    public GitTheoryAttribute(int major, int minor) => Skip = SuiteGit.Skip(major, minor);
}

// A test of what Git did before the given version, skipped from it on.
internal sealed class GitBeforeFactAttribute : FactAttribute
{
    public GitBeforeFactAttribute(int major, int minor) => Skip = SuiteGit.SkipFrom(major, minor);
}

internal sealed class GitBeforeTheoryAttribute : TheoryAttribute
{
    public GitBeforeTheoryAttribute(int major, int minor) => Skip = SuiteGit.SkipFrom(major, minor);
}
