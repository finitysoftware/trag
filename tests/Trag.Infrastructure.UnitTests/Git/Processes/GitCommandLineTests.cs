using System.Diagnostics;
using System.Globalization;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.UnitTests.Git.Processes;

// The command line is decided before Git starts, so the start info is the whole assertion; the only Git run here reads a
// harmless value for the environment. The tests set the variables in this process's own environment, so they run alone.
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class GitCommandLineTests
{
    private static readonly GitCommandLine CommandLine = new(new GitExecutable());

    private static ProcessStartInfo WithEnvironment(IReadOnlyDictionary<string, string?> variables, Func<ProcessStartInfo> create)
    {
        var previous = variables.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        foreach (var (key, value) in variables) Environment.SetEnvironmentVariable(key, value);
        try { return create(); }
        finally { foreach (var (key, value) in previous) Environment.SetEnvironmentVariable(key, value); }
    }

    // A caller that overrides a default, removes an inherited variable and adds its own, all in one call.
    private static ProcessStartInfo WithCallerEntries() =>
        WithEnvironment(new Dictionary<string, string?> { ["TRAG_TEST_LINE_REMOVE"] = "present" }, () => CommandLine.Create("C:/repo", ["log"],
            new Dictionary<string, string?> { ["LC_ALL"] = "C.UTF-8", ["TRAG_TEST_LINE_REMOVE"] = null, ["TRAG_TEST_LINE_ADD"] = "added" }));

    // On Windows every command also lets Git reach paths past MAX_PATH, which LongPathTests prove through real Git. Every
    // command takes and gives commit messages in UTF-8, whatever commit encoding the user set, which the cp1250 tests of
    // the writers prove through real Git.
    private static readonly string[] Settings = [.. OperatingSystem.IsWindows() ? ["-c", "core.longpaths=true"] : Array.Empty<string>(), "-c", "i18n.commitEncoding=UTF-8"];

    [Fact]
    public void WhenACommandIsCreated_ShouldTurnThePagerOffFirstAndReadPathspecsLiterally()
    {
        var literal = CommandLine.Create("C:/repo", ["status", "--porcelain=v2"], null);
        Assert.Equal(["--no-pager", .. Settings, "--literal-pathspecs", "status", "--porcelain=v2"], literal.ArgumentList);
        Assert.Equal("C:/repo", literal.WorkingDirectory);
    }

    [Fact]
    public void WhenLiteralPathspecsAreNotAskedFor_ShouldLeaveThemOutButKeepThePagerOff()
    {
        var loose = CommandLine.Create("C:/repo", ["stash", "push", "--include-untracked"], null, literalPathspecs: false);
        Assert.Equal(["--no-pager", .. Settings, "stash", "push", "--include-untracked"], loose.ArgumentList);
    }

    [Fact]
    public void WhenTheLaunchingTerminalSetsRepositoryRedirections_ShouldDropThemAndStillFixPromptsLocksAndLocale()
    {
        var redirection = new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR", "GIT_INDEX_FILE", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_NAMESPACE", "GIT_PREFIX" };
        var start = WithEnvironment(redirection.ToDictionary(key => key, string? (_) => "poison"), () => CommandLine.Create("C:/repo", ["status"], null));

        Assert.DoesNotContain(redirection, start.Environment.ContainsKey);
        Assert.Equal("0", start.Environment["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("0", start.Environment["GIT_OPTIONAL_LOCKS"]);
        Assert.Equal("C", start.Environment["LC_ALL"]);
    }

    // Git applies these to every pathspec of every command: it refuses them beside --literal-pathspecs, and check-ignore
    // refuses literal magic, so inherited from a terminal they would stop every command that names a path.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WhenTheLaunchingTerminalSetsGlobalPathspecMagic_ShouldDropIt(bool literalPathspecs)
    {
        var magic = new[] { "GIT_LITERAL_PATHSPECS", "GIT_GLOB_PATHSPECS", "GIT_NOGLOB_PATHSPECS", "GIT_ICASE_PATHSPECS" };
        var start = WithEnvironment(magic.ToDictionary(key => key, string? (_) => "1"), () => CommandLine.Create("C:/repo", ["add", "--", "a.txt"], null, literalPathspecs));

        Assert.DoesNotContain(magic, start.Environment.ContainsKey);
    }

    // Git hands the -c values of one command on to the alias or hook it runs, so a Trag started from one would apply them
    // above the local configuration of every repository it opens.
    [Fact]
    public void WhenTheLaunchingGitHandsOnItsCommandLineConfiguration_ShouldDropIt()
    {
        var parameters = new Dictionary<string, string?> { ["GIT_CONFIG_PARAMETERS"] = "'trag.inherited'='parameters'" };
        var start = WithEnvironment(parameters, () => CommandLine.Create("C:/repo", ["status"], null));

        Assert.False(start.Environment.ContainsKey("GIT_CONFIG_PARAMETERS"));
    }

    // Git hands its exec path on to every alias and hook it runs, so a Trag started from one would make the Git it chose
    // run another installation's helpers. The value is this Git's own, so a Git another test starts meanwhile is unchanged.
    [Fact]
    public void WhenTheLaunchingGitExportsItsExecPath_ShouldDropIt()
    {
        var execPath = new Dictionary<string, string?> { ["GIT_EXEC_PATH"] = ExecPathOfThisGit() };
        var start = WithEnvironment(execPath, () => CommandLine.Create("C:/repo", ["status"], null));

        Assert.False(start.Environment.ContainsKey("GIT_EXEC_PATH"));
    }

    private static string ExecPathOfThisGit()
    {
        using var git = Process.Start(new ProcessStartInfo(CommandLine.Program, "--exec-path") { RedirectStandardOutput = true })!;
        var execPath = git.StandardOutput.ReadToEnd().TrimEnd();
        git.WaitForExit();
        return execPath;
    }

    // Git's config command reads and writes only the file GIT_CONFIG names and refuses --local beside it, so inherited it
    // would point Trag's configuration reads at another file and fail every --local one. The file is empty and exists.
    [Fact]
    public void WhenTheLaunchingTerminalPointsGitConfigAtAnotherFile_ShouldDropIt()
    {
        var file = Path.GetTempFileName();
        try
        {
            var start = WithEnvironment(new Dictionary<string, string?> { ["GIT_CONFIG"] = file }, () => CommandLine.Create("C:/repo", ["config", "--get", "user.name"], null));

            Assert.False(start.Environment.ContainsKey("GIT_CONFIG"));
        }
        finally { File.Delete(file); }
    }

    // Git takes the author and committer from these before the configuration, as in the user's terminal, and Trag shows
    // the identity Git reads. Git refuses "now" for a date, so the date is the current time in Git's own form.
    [Fact]
    public void WhenTheLaunchingTerminalSetsTheUsersIdentity_ShouldPassItToGit()
    {
        var now = DateTimeOffset.Now;
        var date = now.ToUnixTimeSeconds() + " " + now.ToString("zzz", CultureInfo.InvariantCulture);
        var identity = new Dictionary<string, string?>
        {
            ["GIT_AUTHOR_NAME"] = "Trag Fixture", ["GIT_AUTHOR_EMAIL"] = "fixture@example.invalid", ["GIT_AUTHOR_DATE"] = date,
            ["GIT_COMMITTER_NAME"] = "Trag Fixture", ["GIT_COMMITTER_EMAIL"] = "fixture@example.invalid", ["GIT_COMMITTER_DATE"] = date
        };
        var start = WithEnvironment(identity, () => CommandLine.Create("C:/repo", ["commit"], null));

        Assert.All(identity, entry => Assert.Equal(entry.Value, start.Environment[entry.Key]));
    }

    // A clone, which has no remote to read the SSH command from yet, takes GIT_SSH_COMMAND from the environment, and every
    // git:// transfer takes GIT_PROXY_COMMAND from it, as in the user's terminal.
    [Theory]
    [InlineData("GIT_SSH_COMMAND", "ssh -i env-key")]
    [InlineData("GIT_PROXY_COMMAND", "env-proxy")]
    public void WhenTheLaunchingTerminalSetsHowGitReachesARemote_ShouldPassItToGit(string variable, string value)
    {
        var start = WithEnvironment(new Dictionary<string, string?> { [variable] = value }, () => CommandLine.Create("C:/repo", ["clone"], null));

        Assert.Equal(value, start.Environment[variable]);
    }

    // The user sets these on purpose, so Git reads them as it does in the user's terminal. The key is set before the count
    // and removed after it, so a Git that another test starts meanwhile never sees a count without its key.
    [Fact]
    public void WhenTheUserSetsConfigurationThroughGitConfigCount_ShouldPassItToGit()
    {
        var entry = new Dictionary<string, string?> { ["GIT_CONFIG_KEY_0"] = "trag.inherited", ["GIT_CONFIG_VALUE_0"] = "count" };
        var count = new Dictionary<string, string?> { ["GIT_CONFIG_COUNT"] = "1" };
        var start = WithEnvironment(entry, () => WithEnvironment(count, () => CommandLine.Create("C:/repo", ["status"], null)));

        Assert.Equal("1", start.Environment["GIT_CONFIG_COUNT"]);
        Assert.Equal("trag.inherited", start.Environment["GIT_CONFIG_KEY_0"]);
        Assert.Equal("count", start.Environment["GIT_CONFIG_VALUE_0"]);
    }

    [Fact]
    public void WhenATraceVariableIsSetForAPlainRun_ShouldKeepIt()
    {
        var trace = new Dictionary<string, string?> { ["GIT_TRACE_TRAGCOMMANDLINE"] = "1" };
        var plain = WithEnvironment(trace, () => CommandLine.Create("C:/repo", ["fetch"], null));
        Assert.Equal("1", plain.Environment["GIT_TRACE_TRAGCOMMANDLINE"]);
    }

    // Only an askpass run could leak a trace to the credential helper, so only there it is dropped.
    [Fact]
    public void WhenATraceVariableIsSetForAnAskpassRun_ShouldDropItAndKeepTheMarker()
    {
        var trace = new Dictionary<string, string?> { ["GIT_TRACE_TRAGCOMMANDLINE"] = "1" };
        var askpass = WithEnvironment(trace, () => CommandLine.Create("C:/repo", ["fetch"], new Dictionary<string, string?> { ["TRAG_ASKPASS"] = "1" }));
        Assert.False(askpass.Environment.ContainsKey("GIT_TRACE_TRAGCOMMANDLINE"));
        Assert.Equal("1", askpass.Environment["TRAG_ASKPASS"]);
    }

    [Fact]
    public void WhenTheCallerGivesItsOwnValues_ShouldOverrideTheDefaultsAndAddItsVariables()
    {
        var start = WithCallerEntries();
        Assert.Equal("C.UTF-8", start.Environment["LC_ALL"]);
        Assert.Equal("added", start.Environment["TRAG_TEST_LINE_ADD"]);
    }

    [Fact]
    public void WhenTheCallerGivesNullForAnInheritedVariable_ShouldLeaveNothingToInherit()
    {
        var start = WithCallerEntries();
        Assert.True(!start.Environment.TryGetValue("TRAG_TEST_LINE_REMOVE", out var removed) || removed is null);
    }
}
