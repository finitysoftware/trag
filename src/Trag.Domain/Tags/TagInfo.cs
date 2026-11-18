namespace Trag.Domain.Tags;

// One tag of the repository as the tag list shows it: its name, the commit it marks, and its subject: an annotated
// tag's own, a lightweight tag's commit's.
public sealed record TagInfo(string Name, string Commit, string Subject);
