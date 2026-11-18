namespace Trag.Domain.Presentation.Text;

// What a Git LFS file's diff shows in place of its pointers.
public static class LfsText
{
    public static string File(long? before, long? after, bool downloaded) =>
        "Git LFS file" + (before is { } old && after is { } now ? " · " + Counted.Size(old) + " → " + Counted.Size(now)
            : (before ?? after) is { } size ? " · " + Counted.Size(size) : "")
        + (downloaded ? "" : ". Its content is not downloaded.");
}
