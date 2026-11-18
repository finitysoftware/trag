namespace Trag.Domain.Presentation;

// What a diff is of: one path at one version, where the version is the index, the working tree or a commit. Two
// presentations with the same source are the same content read again, so a view keeps its place across them; the path
// alone is not enough, since the index and the working tree of one file are different content.
public sealed record DiffSource(string Path, string Version);
