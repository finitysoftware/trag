using Trag.Domain.Setup;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Setup;

namespace Trag.Infrastructure.UnitTests.Git.Setup;

// A chosen git.exe that answers --version with more than Trag reads of any Git output is no Git, as any other program that
// does not answer as Git, rather than failing Settings or the Git setup that chose it; here the "Git" is Trag.GitStub
// copied as git.exe and told to print such an answer.
public sealed class GitLocatorChooseTests
{
    [Fact]
    public async Task WhenTheChosenGitAnswersWithMoreThanTheOutputLimit_ShouldCallItNoGit()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "trag-flood-git-" + Guid.NewGuid().ToString("N"))).FullName;
        Environment.SetEnvironmentVariable("TRAG_GITSTUB_FLOOD", (9 * 1024 * 1024).ToString());
        try
        {
            foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "Trag.GitStub*"))
                File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
            var extension = OperatingSystem.IsWindows() ? ".exe" : "";
            var git = Path.Combine(folder, "git" + extension);
            File.Move(Path.Combine(folder, "Trag.GitStub" + extension), git);

            var finding = await new GitLocator(new GitExecutable()).FindAsync(git, CancellationToken.None);

            Assert.Equal(new GitFinding(GitProblem.NotGit), finding);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TRAG_GITSTUB_FLOOD", null);
            Directory.Delete(folder, true);
        }
    }
}
