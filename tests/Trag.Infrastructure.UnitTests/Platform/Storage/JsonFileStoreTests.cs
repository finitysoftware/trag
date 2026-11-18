using Trag.Infrastructure.Platform.Storage;

namespace Trag.Infrastructure.UnitTests.Platform.Storage;

// The discipline every settings file on disk follows, now that it lives in one type: a missing file is an empty
// answer, an oversized one is refused before it is parsed, and a save either replaces the whole file or leaves the
// previous one. The catalog document stands in for any document; what a document means is tested with its own store.
public sealed class JsonFileStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "trag-json-" + Guid.NewGuid().ToString("N"));
    public JsonFileStoreTests() => Directory.CreateDirectory(directory);
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    private JsonFileStore<RepositoryCatalogDocument> Store(string name = "document.json", long maximumBytes = 1024 * 1024) =>
        new(Path.Combine(directory, name), StorageJsonContext.Default.RepositoryCatalogDocument, maximumBytes);

    private static RepositoryCatalogDocument Document(string path) => new([new(path, "name", DateTimeOffset.UnixEpoch)]);

    [Fact]
    public async Task WhenTheFileIsMissing_ShouldAnswerEmptyRatherThanFail() => Assert.Null(await Store().LoadAsync(CancellationToken.None));

    [Fact]
    public async Task WhenTheFileIsOversized_ShouldRefuseItBeforeParsingIt()
    {
        var path = Path.Combine(directory, "big.json");
        await File.WriteAllTextAsync(path, new string('x', 200));

        await Assert.ThrowsAsync<IOException>(() => Store("big.json", maximumBytes: 100).LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WhenADocumentIsSavedIntoAMissingFolder_ShouldCreateTheFolderAndLoadTheDocumentBack()
    {
        var store = new JsonFileStore<RepositoryCatalogDocument>(Path.Combine(directory, "nested", "document.json"),
            StorageJsonContext.Default.RepositoryCatalogDocument, 1024 * 1024);

        await store.SaveAsync(Document("C:/one"), CancellationToken.None);

        var loaded = await store.LoadAsync(CancellationToken.None);
        Assert.Equal("C:/one", Assert.Single(loaded!.Repositories).Path);
    }

    [Fact]
    public async Task WhenASaveIsCancelled_ShouldKeepThePreviousFileAndLeaveNoTemporaryOne()
    {
        var store = Store();
        await store.SaveAsync(Document("C:/first"), CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(Document("C:/second"), cancelled.Token));

        Assert.Equal("C:/first", Assert.Single((await store.LoadAsync(CancellationToken.None))!.Repositories).Path);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task WhenSavesRunConcurrently_ShouldRunThemOneAtATimeAndNeverLeaveTheFileHalfWritten()
    {
        var store = Store();

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => store.SaveAsync(Document("C:/repo-" + i), CancellationToken.None)));

        Assert.StartsWith("C:/repo-", Assert.Single((await store.LoadAsync(CancellationToken.None))!.Repositories).Path);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    // Two instances of Trag each hold the file through a store of their own: a change waits until the other one's is
    // written, and is made to what that one wrote.
    [Fact]
    public async Task WhenTwoProcessesSaveTheSameFile_ShouldNotLoseTheOthersEntries()
    {
        var (first, second) = (Store(), Store());
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var one = first.UpdateAsync(document => { entered.SetResult(); release.Task.Wait(); return With(document, "C:/one"); }, CancellationToken.None);
        await entered.Task;

        var two = second.UpdateAsync(document => With(document, "C:/two"), CancellationToken.None);
        await Task.WhenAny(two, Task.Delay(500));
        release.SetResult();
        await Task.WhenAll(one, two);

        Assert.Equal(["C:/one", "C:/two"], (await Store().LoadAsync(CancellationToken.None))!.Repositories.Select(entry => entry.Path).Order());
    }

    private static RepositoryCatalogDocument With(RepositoryCatalogDocument? document, string path) =>
        new([.. document?.Repositories ?? [], new(path, "name", DateTimeOffset.UnixEpoch)]);

    // A file that is not JSON is the owning store's failure to explain; the file itself stays as it was for the user to look at.
    [Fact]
    public async Task WhenTheFileHoldsInvalidJson_ShouldThrowJsonExceptionAndLeaveTheFile()
    {
        var path = Path.Combine(directory, "document.json");
        await File.WriteAllTextAsync(path, "{ not json");

        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => Store().LoadAsync(CancellationToken.None));

        Assert.Equal("{ not json", await File.ReadAllTextAsync(path));
    }

    // On Windows another program holding the file open, or a file marked read-only, fails the save: the previous file
    // stays and no temporary one is left behind. Elsewhere neither stops a file from being replaced.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenTheTargetCannotBeReplaced_ShouldFailTheSaveAndKeepThePreviousFile(bool heldOpen)
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = Store();
        await store.SaveAsync(Document("C:/first"), CancellationToken.None);
        var path = Path.Combine(directory, "document.json");
        using var held = heldOpen ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
        if (!heldOpen) File.SetAttributes(path, FileAttributes.ReadOnly);

        var failure = await Record.ExceptionAsync(() => store.SaveAsync(Document("C:/second"), CancellationToken.None));

        held?.Dispose();
        File.SetAttributes(path, FileAttributes.Normal);
        Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
        Assert.Equal("C:/first", Assert.Single((await store.LoadAsync(CancellationToken.None))!.Repositories).Path);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }
}
