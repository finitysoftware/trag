using Trag.Domain.Merge;

namespace Trag.ViewModels.Merge;

// One piece of the shown conflict with a block's lines in the order its cards show them: yours first, then the other side.
public sealed record ConflictPartRow(ConflictPart Part, IReadOnlyList<ConflictLine> Yours, IReadOnlyList<ConflictLine> Other);
