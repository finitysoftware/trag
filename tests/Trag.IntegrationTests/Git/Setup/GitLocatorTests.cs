using Trag.Domain.Naming;
using Trag.Domain.Setup;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Setup;

namespace Trag.IntegrationTests.Git.Setup;

// The locator on this machine: it takes the Git of the PATH, or the git.exe the user chose, as the one every runner that
// shares its executable starts from then on; a chosen file that does not run as Git is no Git, and changes nothing.
public sealed class GitLocatorTests
{
    private static CancellationToken Timeout() => new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;

    [Fact]
    public async Task WhenGitIsOnThePath_ShouldTakeItForEveryRunnerThatSharesTheExecutable()
    {
        var executable = new GitExecutable();

        var finding = await new GitLocator(executable).FindAsync(null, Timeout());

        Assert.Equal(GitProblem.None, finding.Problem);
        var git = finding.Git!;
        Assert.Equal((GitOrigin.Path, true, true), (git.Origin, Path.IsPathFullyQualified(git.Path), git.Version >= GitLimits.MinimumGit));
        Assert.Equal(git.Path, executable.Path);
    }

    [Fact]
    public async Task WhenAWorkingGitIsChosen_ShouldTakeItAsTheChosenOne()
    {
        var path = (await new GitLocator(new GitExecutable()).FindAsync(null, Timeout())).Git!.Path;
        var executable = new GitExecutable();

        var finding = await new GitLocator(executable).FindAsync(path, Timeout());

        Assert.Equal((GitProblem.None, GitOrigin.Chosen, path), (finding.Problem, finding.Git!.Origin, executable.Path));
    }

    [Fact]
    public async Task WhenTheGitIsDescribed_ShouldSayWhatItsOwnConfigurationSays()
    {
        var executable = new GitExecutable();
        var locator = new GitLocator(executable);
        var git = (await locator.FindAsync(null, Timeout())).Git!;
        var runner = new GitProcessRunner(executable: executable);
        async Task<string> Get(string key) => System.Text.Encoding.UTF8.GetString((await runner.RunAsync(Path.GetTempPath(), ["config", "--get", key], Timeout())).StandardOutput).Trim();

        var features = (await locator.DescribeAsync(git, Timeout()))!;

        Assert.Equal((await Get("filter.lfs.process")).Length != 0, features.Lfs);
        Assert.Equal(await Get("credential.helper") is { Length: > 0 } helper ? helper : null, features.CredentialHelper);
    }

    [Fact]
    public async Task WhenTheChosenGitDoesNotRun_ShouldCallItNoGitAndKeepTheExecutable()
    {
        var folder = Path.Combine(Path.GetTempPath(), "trag-no-git-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var fake = Path.Combine(folder, OperatingSystem.IsWindows() ? "git.exe" : "git");
        await File.WriteAllTextAsync(fake, "not a program");
        var executable = new GitExecutable();
        try
        {
            var finding = await new GitLocator(executable).FindAsync(fake, Timeout());

            Assert.Equal(new GitFinding(GitProblem.NotGit), finding);
            Assert.Equal("git", executable.Path);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
