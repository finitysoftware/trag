using Trag.Infrastructure.Platform.Storage;

namespace Trag.Infrastructure.UnitTests.Platform.Storage;

// The git.exe the user chose is kept beside the settings, for the next start, until the user lets Trag find Git again.
public sealed class GitChoiceStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "trag-git-choice-" + Guid.NewGuid().ToString("N"));
    public GitChoiceStoreTests() => Directory.CreateDirectory(directory);
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Fact]
    public async Task WhenAGitIsChosen_ShouldKeepItForTheNextStartUntilTheChoiceIsCleared()
    {
        var file = Path.Combine(directory, "git.json");
        var chosen = Path.Combine(directory, "PortableGit", "cmd", "git.exe");
        Assert.Null(await new GitChoiceStore(file).LoadAsync(CancellationToken.None));

        await new GitChoiceStore(file).SaveAsync(chosen, CancellationToken.None);
        Assert.Equal(chosen, await new GitChoiceStore(file).LoadAsync(CancellationToken.None));
        await new GitChoiceStore(file).SaveAsync(null, CancellationToken.None);

        Assert.Null(await new GitChoiceStore(file).LoadAsync(CancellationToken.None));
    }
}
