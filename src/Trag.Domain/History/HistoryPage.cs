namespace Trag.Domain.History;

public sealed record HistoryPage(IReadOnlyList<HistoryCommit> Commits, bool HasMore, int Number);
