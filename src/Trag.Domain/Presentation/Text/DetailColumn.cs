namespace Trag.Domain.Presentation.Text;

// One column of what a repository holds in the Repository details dialog.
public sealed record DetailColumn(IReadOnlyList<DetailFact> Facts);
