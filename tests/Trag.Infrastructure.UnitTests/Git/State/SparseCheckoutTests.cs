using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.State;

// A sparse checkout leaves the files outside its cone out of the working tree and marks them skip-worktree in the index.
// Trag reads such a repository and does not write to it yet.
public sealed class SparseCheckoutTests
{
    // top.txt, in/a.txt, out/b.txt and out/deep/c.txt on main, a branch other that changes out/b.txt, and main checked out
    // with the cone on in/ alone.
    private static async Task<GitFixture> ConeAsync(bool sparseIndex)
    {
        var f = await GitFixture.CreateAsync("main");
        try
        {
            await f.GitAsync("config", "core.autocrlf", "false");
            await f.WriteAsync("top.txt", "top\n"); await f.WriteAsync("in/a.txt", "a\n");
            await f.WriteAsync("out/b.txt", "b\n"); await f.WriteAsync("out/deep/c.txt", "c\n");
            await f.CommitAsync("Base");
            await f.GitAsync("switch", "-c", "other");
            await f.WriteAsync("out/b.txt", "other\n"); await f.CommitAsync("Other");
            await f.GitAsync("switch", "main");
            await f.GitAsync(["sparse-checkout", "set", "--cone", .. sparseIndex ? new[] { "--sparse-index" } : [], "in"]);
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    private static string IndexOf(GitFixture f) => Path.Combine(f.Root, ".git", "index");
    private static bool OutsideIsThere(GitFixture f) => Directory.Exists(Path.Combine(f.Root, "out"));

    private static IEnumerable<(string, char, char)> Listed(RepositorySnapshot snapshot) =>
        snapshot.Files.Select(file => (file.Path.Display, file.Index, file.Worktree));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenFilesLieOutsideTheCone_ShouldListOnlyTheChangesInsideItUnderTheSparseCheckoutLimitation(bool sparseIndex)
    {
        using var f = await ConeAsync(sparseIndex);
        await f.WriteAsync("in/a.txt", "edited\n");

        var snapshot = await f.ReadAsync();

        Assert.False(OutsideIsThere(f));
        Assert.Equal([("in/a.txt", '.', 'M')], Listed(snapshot));
        Assert.Equal([LimitationText.SparseCheckout], snapshot.Limitations);
    }

    // Git takes an out-of-cone file that is there again for a tracked file, but only in the memory of that one status: Trag's
    // reads write no index, so the skip-worktree bit stays and the file is not listed as deleted once it goes again.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenAFileOutsideTheConeAppearsAndGoesAgain_ShouldNotListItAsDeleted(bool sparseIndex)
    {
        using var f = await ConeAsync(sparseIndex);
        var index = await File.ReadAllBytesAsync(IndexOf(f), f.Token);
        await f.WriteAsync("out/b.txt", "here again\n");

        var appeared = await f.ReadAsync();
        File.Delete(Path.Combine(f.Root, "out", "b.txt"));
        var gone = await f.ReadAsync();

        Assert.Equal([("out/b.txt", '.', 'M')], Listed(appeared));
        Assert.Empty(gone.Files);
        Assert.Equal(index, await File.ReadAllBytesAsync(IndexOf(f), f.Token));
    }

    // Every write is refused under the limitation, so none of them reaches a skip-worktree entry: the index, the cone,
    // HEAD, the branch and the working files stay as they were.
    [Theory]
    [InlineData("stage")]
    [InlineData("commit")]
    [InlineData("discard")]
    [InlineData("switch")]
    public async Task WhenAWriteIsAskedInASparseCheckout_ShouldRefuseItAndLeaveTheIndexAndTheWorkingTreeAsTheyWere(string write)
    {
        using var f = await ConeAsync(sparseIndex: false);
        await f.WriteAsync("top.txt", "staged\n"); await f.GitAsync("add", "top.txt");
        await f.WriteAsync("in/a.txt", "edited\n");
        var before = await UntouchedAsync(f);
        var snapshot = await f.ReadAsync();
        GitWriteRequest request = write switch
        {
            "stage" => new StageRequest(snapshot, [Snapshots.Path("in/a.txt")]),
            "commit" => new CommitRequest(snapshot, "Commit in a cone"),
            "discard" => new DiscardRequest(snapshot, [Snapshots.Path("in/a.txt")]),
            _ => new BranchRequest(snapshot, new("other"), BranchAction.Switch)
        };

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(request, f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, WriteText.StateUnsupported), (result.Outcome, result.Message));
        Assert.Equal(before, await UntouchedAsync(f));
        Assert.Equal("edited\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "in", "a.txt"), f.Token));
        Assert.False(OutsideIsThere(f));
    }

    // What a refused write leaves as it was: the index and the cone byte for byte, HEAD and the branch.
    private static async Task<(string Index, string Cone, string Head, string Branch)> UntouchedAsync(GitFixture f) => (
        Convert.ToHexString(await File.ReadAllBytesAsync(IndexOf(f), f.Token)),
        Convert.ToHexString(await File.ReadAllBytesAsync(Path.Combine(f.Root, ".git", "info", "sparse-checkout"), f.Token)),
        await f.GitAsync("rev-parse", "HEAD"), await f.GitAsync("symbolic-ref", "--short", "HEAD"));
}
