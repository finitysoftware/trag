using Trag.Domain.Paths;

namespace Trag.Domain.Naming;

// One way to ignore an untracked file: the line of the repository's .gitignore and what the menu calls it.
public sealed record IgnoreChoice(GitPath Path, string Pattern, string Label);
