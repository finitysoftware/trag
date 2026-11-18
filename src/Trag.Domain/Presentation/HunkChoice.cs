using Trag.Domain.Diff;

namespace Trag.Domain.Presentation;

// Lines chosen to take apart from the rest of their file, with the diff they were chosen in: the diff shown can be read
// again before the choice is acted on, and the same numbers would then name other lines.
public sealed record HunkChoice(IReadOnlyList<PatchLine> Document, IReadOnlyList<int> Lines);
