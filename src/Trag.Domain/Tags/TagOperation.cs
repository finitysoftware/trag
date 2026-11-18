namespace Trag.Domain.Tags;

// A tag to create at a commit: with a message Git keeps an annotated tag, without one a lightweight tag. Git validates the
// name (check-ref-format) and the commit before anything changes.
public sealed record TagOperation(string Name, string Commit, string Message = "")
{
    // A repository that signs its tags makes annotated ones only, so a tag there needs a message.
    public static bool LacksMessage(bool signsTags, string message) => signsTags && string.IsNullOrWhiteSpace(message);
}
