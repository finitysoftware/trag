namespace Trag.Domain.History;

// Incoming edges run from the top of the row to its centre; outgoing edges from the centre to the bottom.
public sealed record GraphRow(int Lane, int Color, IReadOnlyList<GraphEdge> Incoming, IReadOnlyList<GraphEdge> Outgoing);
