namespace Trag.Domain.History;

public sealed class CommitGraph(IReadOnlyDictionary<string, GraphRow> rows, int laneCount)
{
    public static readonly CommitGraph Empty = new(new Dictionary<string, GraphRow>(StringComparer.Ordinal), 0);
    public IReadOnlyDictionary<string, GraphRow> Rows { get; } = rows;
    public int LaneCount { get; } = laneCount;
    public GraphRow? For(string id) => Rows.GetValueOrDefault(id);
}
