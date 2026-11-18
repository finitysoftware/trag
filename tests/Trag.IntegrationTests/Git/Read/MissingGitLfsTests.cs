using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// Git LFS set up in the configuration, as git lfs install leaves it, while the Git Trag runs finds no git-lfs, as a
// MinGit without it does. Git for Windows installs git-lfs beside its git.exe, so such a Git is Trag.GitStub, which
// answers git lfs as Git does without it and hands every other command to the real one; its mode goes into this
// process's environment, so the test runs alone.
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class MissingGitLfsTests
{
    private static readonly string GitWithoutLfs = Path.Combine(AppContext.BaseDirectory, "Trag.GitStub" + (OperatingSystem.IsWindows() ? ".exe" : ""));

    [Fact]
    public async Task WhenFilterLfsProcessIsSetButGitLfsIsMissing_ShouldSaySo()
    {
        using var f = await GitFixture.CreateAsync();
        await LfsConfig.SetAsync(f, f.Root, installed: false);
        await f.WriteAsync(".gitattributes", "*.bin filter=lfs\n");
        await f.WriteAsync("asset.bin", "asset\n");
        // Older than the index, the file is clean by its stat data alone; a racily clean one would make the status run
        // the filter, and without git-lfs that read fails.
        File.SetLastWriteTimeUtc(Path.Combine(f.Root, "asset.bin"), DateTime.UtcNow.AddHours(-1));
        await f.CommitAsync("Asset");
        await LfsConfig.SetAsync(f, f.Root, installed: true);
        var reader = new CliRepositoryReader(new GitProcessRunner(executable: new() { Path = GitWithoutLfs }));
        using var stub = new EnvironmentVariables(("TRAG_GIT_STUB", "no-lfs"));

        var snapshot = await reader.ReadAsync(f.Root, f.Token);

        Assert.Equal([LimitationText.LfsNotInstalled], snapshot.Limitations);
        Assert.False(snapshot.LfsInstalled);
    }
}
