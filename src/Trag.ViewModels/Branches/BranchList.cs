using System.Runtime.CompilerServices;
using Trag.Domain.Branches;

namespace Trag.ViewModels.Branches;

// The picker's rows over a BranchCatalog: one BranchRow per branch of the read, reused by every filtered view of the
// same read, so a row keeps its identity while the user types. There is one list per read: the picker and every dialog
// that names branches show the same rows for the same read.
public sealed class BranchList
{
    private static readonly ConditionalWeakTable<BranchCatalog, BranchList> Lists = new();
    public static readonly BranchList Empty = Of(BranchCatalog.Empty);
    private readonly IReadOnlyDictionary<BranchInfo, BranchRow> rows;

    private BranchList(BranchCatalog catalog, IReadOnlyDictionary<BranchInfo, BranchRow> rows)
    {
        Catalog = catalog;
        this.rows = rows;
        Current = catalog.Current is { } current ? rows[current] : null;
        Local = catalog.Local.Select(branch => rows[branch]).ToArray();
        RemoteGroups = catalog.RemoteGroups.Select(group => new BranchGroup(group.Remote, group.Branches.Select(branch => rows[branch]).ToArray())).ToArray();
    }

    public BranchCatalog Catalog { get; }
    public BranchRow? Current { get; }
    public IReadOnlyList<BranchRow> Local { get; }
    public IReadOnlyList<BranchGroup> RemoteGroups { get; }
    public bool HasCurrent => Current is not null;
    public bool HasLocal => Local.Count != 0;
    public bool HasAny => HasCurrent || HasLocal || RemoteGroups.Count != 0;
    public IEnumerable<BranchRow> Remote => RemoteGroups.SelectMany(group => group.Rows);
    // The row of a branch of this read, filtered or not.
    public BranchRow Row(BranchInfo branch) => rows[branch];

    // The rows of one read of the repository's branches.
    public static BranchList Of(BranchCatalog catalog) => Lists.GetValue(catalog, Create);

    private static BranchList Create(BranchCatalog catalog)
    {
        var rows = new Dictionary<BranchInfo, BranchRow>(ReferenceEqualityComparer.Instance);
        if (catalog.Current is { } current) rows[current] = Row(catalog, current, true);
        foreach (var branch in catalog.Local.Concat(catalog.Remote)) rows[branch] = Row(catalog, branch, false);
        return new(catalog, rows);
    }

    private static BranchRow Row(BranchCatalog catalog, BranchInfo branch, bool isCurrent) =>
        PullRequestPage.For(branch, catalog.Remotes) is { } page
            ? new(branch, isCurrent) { Request = page, RequestProvider = page.Provider }
            : new(branch, isCurrent) { RequestProvider = catalog.RequestProvider };

    public BranchList Filter(string search)
    {
        var filtered = Catalog.Filter(search);
        return ReferenceEquals(filtered, Catalog) ? this : new(filtered, rows);
    }
}
