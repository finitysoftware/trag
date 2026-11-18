using Trag.Domain.Branches;
using Trag.TestSupport;
using Trag.ViewModels.Branches;

namespace Trag.ViewModels.UnitTests.Branches;

// The rows over a BranchCatalog; the ordering, grouping and filtering rules are tested in Trag.Domain.UnitTests.
public sealed class BranchListTests
{
    [Fact]
    public void WhenBranchRowsAreBuiltAndFiltered_ShouldMarkOnlyTheCurrentRowAndKeepEachRowsIdentity()
    {
        var set = new BranchSet(
            [Local("fix/Two"), Local("main"), Local("feature/One")],
            [Remote("upstream/Two", "upstream"), Remote("origin/One", "origin"), Remote("origin/main", "origin")], "main");
        var all = BranchList.Of(BranchCatalog.Create(set, Snapshots.Create(branch: null)));
        Assert.True(all.Current!.IsCurrent);
        Assert.All(all.Local.Concat(all.Remote), row => Assert.False(row.IsCurrent));
        Assert.Equal(["REMOTE · origin", "REMOTE · upstream"], all.RemoteGroups.Select(group => group.Title));

        var visible = all.Filter("one");
        Assert.Null(visible.Current); Assert.False(visible.HasCurrent); Assert.True(visible.HasAny);
        Assert.Same(all.Local[0], Assert.Single(visible.Local));
        Assert.Same(all.RemoteGroups[0].Rows[1], Assert.Single(visible.Remote));
        Assert.Same(all, all.Filter(" "));
        Assert.False(BranchList.Empty.HasAny);
    }

    private static BranchInfo Local(string name) => new(name, "refs/heads/" + name, Snapshots.HeadId, false);
    private static BranchInfo Remote(string name, string? remote) => new(name, "refs/remotes/" + name, Snapshots.HeadId, true, remote);
}
