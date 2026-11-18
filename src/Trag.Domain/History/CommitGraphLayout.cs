namespace Trag.Domain.History;

// The history in topological order, added in batches as it is read: the lanes still open after a batch go on into the
// next, and each graph returned holds every row added so far. Rows are only ever added, so a graph returned earlier
// still draws its own commits as before. A lane's color is its column, so lanes side by side never share one (git log
// --graph instead cycles its colors).
public sealed class CommitGraphLayout
{
    private readonly Dictionary<string, GraphRow> rows = new(StringComparer.Ordinal);
    // The commit each lane waits for.
    private readonly List<string?> lanes = [];
    private int width;

    // One batch on its own: the lanes still open after it are drawn running on below it.
    public static CommitGraph Build(IReadOnlyList<HistoryCommit> commits) => new CommitGraphLayout().Add(commits);

    public CommitGraph Add(IReadOnlyList<HistoryCommit> commits)
    {
        foreach (var commit in commits) Add(commit);
        return new(rows, width);
    }

    private void Add(HistoryCommit commit)
    {
        if (rows.ContainsKey(commit.Id)) return;
        var waiting = WaitingFor(commit.Id);
        var node = NodeFor(commit.Id, waiting);
        var incoming = Incoming(node, waiting);
        foreach (var lane in waiting)
            if (lane != node) lanes[lane] = null;

        var outgoing = Continuing(node);
        ConnectParents(commit.Parents, node, outgoing);
        width = Math.Max(width, Math.Max(node + 1, Math.Max(Width(incoming), Width(outgoing))));
        rows[commit.Id] = new(node, node, incoming, outgoing);
        Trim();
    }

    private List<int> WaitingFor(string id)
    {
        var waiting = new List<int>();
        for (var i = 0; i < lanes.Count; i++)
            if (lanes[i] == id) waiting.Add(i);
        return waiting;
    }

    private int NodeFor(string id, List<int> waiting)
    {
        if (waiting.Count > 0) return waiting[0];
        var node = Allocate();
        lanes[node] = id;
        return node;
    }

    private List<GraphEdge> Incoming(int node, List<int> waiting)
    {
        var edges = new List<GraphEdge>();
        for (var i = 0; i < lanes.Count; i++)
        {
            if (lanes[i] is null) continue;
            if (i == node && waiting.Count == 0) continue;
            edges.Add(new(i, waiting.Contains(i) ? node : i, i));
        }
        return edges;
    }

    private List<GraphEdge> Continuing(int node)
    {
        var edges = new List<GraphEdge>();
        for (var i = 0; i < lanes.Count; i++)
            if (i != node && lanes[i] is not null) edges.Add(new(i, i, i));
        return edges;
    }

    private void ConnectParents(IReadOnlyList<string> parents, int node, List<GraphEdge> edges)
    {
        if (parents.Count == 0)
        {
            lanes[node] = null;
            return;
        }

        ConnectFirstParent(parents[0], node, edges);
        for (var i = 1; i < parents.Count; i++) ConnectParent(parents[i], node, edges);
    }

    // Keep the main line on the left; join an existing lane only when it is further left.
    private void ConnectFirstParent(string parent, int node, List<GraphEdge> edges)
    {
        var first = Find(parent, node);
        if (first >= 0 && first < node)
        {
            edges.Add(new(node, first, first));
            lanes[node] = null;
            return;
        }
        lanes[node] = parent;
        edges.Add(new(node, node, node));
    }

    private void ConnectParent(string parent, int node, List<GraphEdge> edges)
    {
        var lane = Find(parent, -1);
        if (lane < 0)
        {
            lane = Allocate();
            lanes[lane] = parent;
        }
        edges.Add(new(node, lane, lane));
    }

    private static int Width(List<GraphEdge> edges) =>
        edges.Count == 0 ? 0 : edges.Max(edge => Math.Max(edge.FromLane, edge.ToLane) + 1);

    private void Trim()
    {
        while (lanes.Count > 0 && lanes[^1] is null) lanes.RemoveAt(lanes.Count - 1);
    }

    private int Allocate()
    {
        for (var i = 0; i < lanes.Count; i++)
            if (lanes[i] is null) return i;
        lanes.Add(null);
        return lanes.Count - 1;
    }

    private int Find(string id, int except)
    {
        for (var i = 0; i < lanes.Count; i++)
            if (i != except && lanes[i] == id) return i;
        return -1;
    }
}
