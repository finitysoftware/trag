using System.Text;
using System.Text.Json;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Platform.Storage;

namespace Trag.Infrastructure.UnitTests.Git.Parsing;

public sealed class RepositoryMetadataTests
{
    [Fact]
    public void WhenARenameSourceIsNamedLikeABranchHeader_ShouldKeepItAsThePathAndTheRealHeader()
    {
        var bytes = Encoding.UTF8.GetBytes("# branch.oid abc\0# branch.head main\0# branch.upstream origin/main\0" +
            "2 R. N... 100644 100644 100644 abc def R100 dest\0# branch.head spoof\0");
        var status = StatusParser.Parse(bytes);
        var header = status.Header();
        Assert.Equal("main", header.Branch);
        Assert.Equal("abc", header.Head);
        Assert.Equal("origin/main", header.Upstream);
        Assert.Equal("# branch.head spoof", Assert.Single(status.Files).OriginalPath!.Display);
    }

    [Fact]
    public void WhenAFileIsUnmerged_ShouldOfferItAsAConflictNotAsOrdinaryStagedContent()
    {
        var file = Assert.Single(StatusParser.Parse("u UU N... 100644 100644 100644 100644 a b c conflict.txt\0"u8.ToArray()).Files);
        Assert.True(file.IsConflict);
        Assert.False(file.IsStaged);
        Assert.True(file.IsUnstaged);
    }

    [Theory]
    [InlineData("https://user:secret@example.invalid/team/repo.git?token=hidden#fragment", "https://example.invalid/team/repo.git")]
    [InlineData("ssh://git@example.invalid:2222/team/repo.git", "ssh://example.invalid:2222/team/repo.git")]
    [InlineData("git@example.invalid:team/repo.git", "example.invalid:team/repo.git")]
    [InlineData("user:secret@example.invalid:team/repo.git", "example.invalid:team/repo.git")]
    [InlineData("D:\\Shared\\me@work\\repo.git", "D:\\Shared\\me@work\\repo.git")]
    [InlineData("//server/share/me@work/repo.git", "//server/share/me@work/repo.git")]
    [InlineData("../backup@2/repo.git", "../backup@2/repo.git")]
    [InlineData("backup@2", "backup@2")]
    public void WhenAnAddressIsPresented_ShouldCarryNoCredentials(string input, string expected) => Assert.Equal(expected, AddressRedactor.Redact(input));

    [Fact]
    public void WhenAConfigurationValueHasNewlines_ShouldKeepThem()
    {
        var entry = Assert.Single(GitConfigParser.Parse("remote.with.dots.url\nfirst\nsecond\0"u8.ToArray()));
        Assert.Equal("remote.with.dots.url", entry.Key);
        Assert.Equal("first\nsecond", entry.Value);
    }

    [Fact]
    public async Task WhenNothingWasSaved_ShouldLoadNoPath()
    {
        var (root, path) = SettingsPath();
        try
        {
            var store = new LastRepositoryStore(path);
            Assert.Null(await store.LoadAsync(CancellationToken.None));
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(root)) Directory.Delete(root); }
    }

    [Fact]
    public async Task WhenAPathIsSaved_ShouldLoadItInAnotherStoreAndStoreNothingElse()
    {
        var (root, path) = SettingsPath();
        try
        {
            await SavedAsync(path);
            Assert.Equal("folder with spaces/č", await new LastRepositoryStore(path).LoadAsync(CancellationToken.None));
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.Single(json.RootElement.EnumerateObject());
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(root)) Directory.Delete(root); }
    }

    [Fact]
    public async Task WhenTheSavedPathIsForgotten_ShouldLoadNoPathAndLeaveOnlyTheSettingsFile()
    {
        var (root, path) = SettingsPath();
        try
        {
            var store = await SavedAsync(path);
            await store.SaveAsync(null, CancellationToken.None);
            Assert.Null(await store.LoadAsync(CancellationToken.None));
            Assert.Single(Directory.GetFiles(root));
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(root)) Directory.Delete(root); }
    }

    [Fact]
    public async Task WhenTheSettingsAreCorrupt_ShouldReportItWithoutOverwritingThem()
    {
        var path = Path.Combine(Path.GetTempPath(), "trag-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, "broken json");
            await Assert.ThrowsAsync<JsonException>(() => new LastRepositoryStore(path).LoadAsync(CancellationToken.None));
            Assert.Equal("broken json", await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    // A settings file in a folder of its own, neither of them there yet.
    private static (string Root, string Settings) SettingsPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "trag-settings-" + Guid.NewGuid().ToString("N"));
        return (root, Path.Combine(root, "settings.json"));
    }

    // The store found nothing, then saved one path with a space and a letter outside ASCII.
    private static async Task<LastRepositoryStore> SavedAsync(string path)
    {
        var store = new LastRepositoryStore(path);
        await store.LoadAsync(CancellationToken.None);
        await store.SaveAsync("folder with spaces/č", CancellationToken.None);
        return store;
    }
}
