using System.Security.Cryptography;
using System.Text;
using Trag.Domain.Diff;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// A Git LFS file's diff is a diff of its pointers: Trag shows the diff of the content they stand for when that is text and
// here, and otherwise the file with its size on each side and whether its content is here, for a staged change, a commit
// and a working file too large for a whole diff.
public sealed class LfsDiffTests
{
    private static async Task<GitFixture> LfsRepositoryAsync()
    {
        var f = await GitFixture.CreateAsync();
        try
        {
            await LfsConfig.SetAsync(f, f.Root, installed: true);
            await f.WriteAsync(".gitattributes", "*.bin filter=lfs diff=lfs merge=lfs -text\n");
            await f.WriteAsync("asset.bin", "fixture\n");
            await f.CommitAsync("Asset", ".gitattributes", "asset.bin");
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    private static string StoredObject(GitFixture f, string content)
    {
        var oid = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        return Path.Combine(f.Root, ".git", "lfs", "objects", oid[..2], oid[2..4], oid);
    }

    [Fact]
    public async Task WhenAnLfsFileBecameBinaryContent_ShouldShowTheFileWithBothSizes()
    {
        using var f = await LfsRepositoryAsync();
        await File.WriteAllBytesAsync(Path.Combine(f.Root, "asset.bin"), [0, 1, 2, 3], f.Token);
        await f.GitAsync("add", "asset.bin");
        var row = (await f.ReadAsync()).Files.Single();

        var diff = await new CliFileDiffReader(f.Runner).ReadAsync(f.Root, row, true, false, f.Token);

        Assert.Equal((DiffContentKind.Unavailable, LfsText.File(8, 4, downloaded: true)), (diff.Kind, diff.Message));
    }

    // A Git LFS extension stores what its clean command made of the file, so the stored content of either side is not the
    // file's, and neither is the size its pointer records: banana is stored as 13 bytes.
    private static async Task DoublingExtensionAsync(GitFixture f)
    {
        await f.GitAsync("config", "lfs.extension.double.clean", "sed s/a/aaa/g");
        await f.GitAsync("config", "lfs.extension.double.smudge", "sed s/aaa/a/g");
        await f.GitAsync("config", "lfs.extension.double.priority", "0");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenAnLfsExtensionChangedTheStoredContent_ShouldShowTheFileWithoutTheSizesItsPointersRecord(bool oldSide)
    {
        using var f = await LfsRepositoryAsync();
        await DoublingExtensionAsync(f);
        await f.WriteAsync("asset.bin", "banana\n");
        await f.GitAsync("add", "asset.bin");
        if (oldSide)
        {
            await f.CommitAsync("Extended", "asset.bin");
            await f.GitAsync("config", "--remove-section", "lfs.extension.double");
            await f.WriteAsync("asset.bin", "fixture\n");
            await f.GitAsync("add", "asset.bin");
        }
        var row = (await f.ReadAsync()).Files.Single();

        var diff = await new CliFileDiffReader(f.Runner).ReadAsync(f.Root, row, true, false, f.Token);

        Assert.Equal((DiffContentKind.Unavailable, LfsText.File(null, null, downloaded: true)), (diff.Kind, diff.Message));
    }

    // Text content that is here shows as Git would show the file itself, staged or in the working tree.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenAnLfsFilesTextContentChanged_ShouldShowTheContentsDiff(bool staged)
    {
        using var f = await LfsRepositoryAsync();
        await f.WriteAsync("asset.bin", "fixture, changed\n");
        if (staged) await f.GitAsync("add", "asset.bin");
        var row = (await f.ReadAsync()).Files.Single();

        var diff = await new CliFileDiffReader(f.Runner).ReadAsync(f.Root, row, staged, false, f.Token);

        Assert.Equal(DiffContentKind.Text, diff.Kind);
        Assert.Equal(["-fixture", "+fixture, changed"], diff.Lines.Where(line => line.IsChange).Select(line => line.Text));
    }

    // Either side's content missing leaves nothing to compare; the file says whether the side shown is here.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenACommitChangedAnLfsFileWhoseContentIsNotHere_ShouldSaySo(bool newSideMissing)
    {
        using var f = await LfsRepositoryAsync();
        await f.WriteAsync("asset.bin", "fixture, changed\n");
        var changed = await f.CommitAsync("Change", "asset.bin");
        File.Delete(StoredObject(f, newSideMissing ? "fixture, changed\n" : "fixture\n"));
        var reader = new CliHistoryReader(f.Runner);
        var details = await reader.ReadCommitAsync(f.Root, changed, f.Token);

        var diff = await reader.ReadDiffAsync(f.Root, details, details.Files.Single(), false, f.Token);

        Assert.Equal(LfsText.File(8, 17, downloaded: !newSideMissing), diff.Message);
    }

    // Git LFS keeps content where lfs.storage says, when it says so.
    [Fact]
    public async Task WhenTheLfsStorageIsElsewhere_ShouldStillShowTheContentsDiff()
    {
        using var f = await GitFixture.CreateAsync();
        await LfsConfig.SetAsync(f, f.Root, installed: true);
        await f.GitAsync("config", "lfs.storage", "elsewhere");
        await f.WriteAsync(".gitattributes", "*.bin filter=lfs diff=lfs merge=lfs -text\n");
        await f.WriteAsync("asset.bin", "fixture\n");
        await f.CommitAsync("Asset", ".gitattributes", "asset.bin");
        await f.WriteAsync("asset.bin", "fixture, changed\n");
        await f.GitAsync("add", "asset.bin");
        var row = (await f.ReadAsync()).Files.Single();

        var diff = await new CliFileDiffReader(f.Runner).ReadAsync(f.Root, row, true, false, f.Token);

        Assert.Equal(["-fixture", "+fixture, changed"], diff.Lines.Where(line => line.IsChange).Select(line => line.Text));
    }

    // The index holds the pointer, so a patch of the content's lines would never apply: the content is shown, and the
    // file is staged, unstaged or discarded whole.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenAnLfsFilesContentDiffIsShown_ShouldOfferNoHunk(bool staged)
    {
        using var f = await LfsRepositoryAsync();
        await f.WriteAsync("asset.bin", "fixture, changed\n");
        if (staged) await f.GitAsync("add", "asset.bin");
        var row = (await f.ReadAsync()).Files.Single();

        var diff = await new CliFileDiffReader(f.Runner).ReadAsync(f.Root, row, staged, false, f.Token);

        Assert.Equal(DiffContentKind.Text, diff.Kind);
        Assert.Same(HunkOffer.None, HunkOffer.For(row, staged, DiffDocument.From(diff, DiffPresentation.Empty).IsCompleteText));
    }

    // A new LFS file's text content is all additions, as Git shows a new file.
    [Fact]
    public async Task WhenANewLfsFileIsStaged_ShouldShowItsContentAsAdded()
    {
        using var f = await LfsRepositoryAsync();
        await f.WriteAsync("new.bin", "new\n");
        await f.GitAsync("add", "new.bin");
        var row = (await f.ReadAsync()).Files.Single();

        var diff = await new CliFileDiffReader(f.Runner).ReadAsync(f.Root, row, true, false, f.Token);

        Assert.Equal(["+new"], diff.Lines.Where(line => line.IsChange).Select(line => line.Text));
    }

    // Content too large for a whole diff is not compared, staged or committed.
    [Fact]
    public async Task WhenStagedLfsTextIsTooLargeForAWholeDiff_ShouldShowTheFileWithBothSizes()
    {
        using var f = await LfsRepositoryAsync();
        var text = string.Concat(Enumerable.Repeat("line\n", DiffLimits.ByteLimit / 5 + 1));
        await f.WriteAsync("asset.bin", text);
        await f.GitAsync("add", "asset.bin");
        var row = (await f.ReadAsync()).Files.Single();

        var diff = await new CliFileDiffReader(f.Runner).ReadAsync(f.Root, row, true, false, f.Token);

        Assert.Equal(LfsText.File(8, text.Length, downloaded: true), diff.Message);
    }

    [Fact]
    public async Task WhenAWorkingLfsFileIsTooLargeForAWholeDiff_ShouldShowTheFileWithBothSizes()
    {
        using var f = await LfsRepositoryAsync();
        var size = DiffLimits.ByteLimit + 1;
        await File.WriteAllBytesAsync(Path.Combine(f.Root, "asset.bin"), new byte[size], f.Token);
        var row = (await f.ReadAsync()).Files.Single();

        var diff = await new CliFileDiffReader(f.Runner).ReadAsync(f.Root, row, false, false, f.Token);

        Assert.Equal(LfsText.File(8, size, downloaded: true), diff.Message);
    }

    // The index's pointer records the size the extension stored, which is not the size the file had when it was staged.
    [Fact]
    public async Task WhenAWorkingLfsFileTooLargeForAWholeDiffWasStagedThroughAnExtension_ShouldShowTheFileWithoutSizes()
    {
        using var f = await LfsRepositoryAsync();
        await DoublingExtensionAsync(f);
        await f.WriteAsync("asset.bin", "banana\n");
        await f.GitAsync("add", "asset.bin");
        await File.WriteAllBytesAsync(Path.Combine(f.Root, "asset.bin"), new byte[DiffLimits.ByteLimit + 1], f.Token);
        var row = (await f.ReadAsync()).Files.Single(file => file.IsUnstaged);

        var diff = await new CliFileDiffReader(f.Runner).ReadAsync(f.Root, row, false, false, f.Token);

        Assert.Equal(LfsText.File(null, null, downloaded: true), diff.Message);
    }

    // A file taken out of Git LFS is staged as its content; too large for a whole diff, it is offered as any large file.
    [Fact]
    public async Task WhenALargeFileTakenOutOfLfsIsStaged_ShouldOfferItAsALargeFile()
    {
        using var f = await LfsRepositoryAsync();
        await f.WriteAsync(".gitattributes", "");
        await File.WriteAllBytesAsync(Path.Combine(f.Root, "asset.bin"), new byte[DiffLimits.ByteLimit + 1], f.Token);
        await f.GitAsync("add", ".gitattributes", "asset.bin");
        var row = (await f.ReadAsync()).Files.Single(file => file.Path.Display == "asset.bin");

        var diff = await new CliFileDiffReader(f.Runner).ReadAsync(f.Root, row, true, false, f.Token);

        Assert.True(diff.CanPreview, diff.Message);
    }
}
