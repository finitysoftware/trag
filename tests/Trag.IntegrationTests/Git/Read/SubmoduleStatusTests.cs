using Trag.Domain.Status;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// Git's status lists a submodule never checked out, its folder empty, with the same fields as one checked out (the index
// changed its gitlink in a clone made without its submodules); only the repository in its folder tells them apart.
public sealed class SubmoduleStatusTests
{
    [Fact]
    public async Task WhenASubmoduleFolderHoldsNoRepository_ShouldListItNotCheckedOutUntilItIsCheckedOut()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var origin = SubmoduleFixture.Origin(f);
        await f.GitAtAsync(origin, "commit", "--allow-empty", "-m", "Sub two");
        var two = await f.GitAtAsync(origin, "rev-parse", "HEAD");
        await f.GitAtAsync(Path.Combine(f.Root, "sub"), "commit", "--allow-empty", "-m", "Sub local");
        await f.GitAtAsync(f.Root, "init", "inner");
        await f.WriteAsync(Path.Combine("inner", "i.txt"), "i\n");
        var clone = Path.Combine(f.Container, "clone");
        await f.GitAsync("clone", "--", f.Root, clone);
        await f.GitAtAsync(clone, "update-index", "--cacheinfo", "160000," + two + ",sub");

        static (string, bool, bool) Row(RepositorySnapshot snapshot, string path)
        {
            var row = snapshot.Files.Single(file => file.Path.Display == path);
            return (row.Path.Display, row.IsSubmodule, row.SubmoduleIsCheckedOut);
        }
        var root = await f.ReadAsync();
        var previous = await f.ReadAsync(clone);
        Assert.Equal(("sub", true, true), Row(root, "sub"));
        Assert.Equal(("inner/", false, false), Row(root, "inner/"));
        Assert.Equal(("sub", true, false), Row(previous, "sub"));

        await f.GitAtAsync(clone, "-c", "protocol.file.allow=always", "submodule", "update", "--init");
        var incremental = await new CliRepositoryReader(new()).ReadPathsAsync(previous, ["sub"], f.Token);

        Assert.Equal(("sub", true, true), Row(incremental, "sub"));
    }
}
