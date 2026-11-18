namespace Trag.Domain.Rebase;

// What a rewrite does with one commit: keeps it, keeps it with a new message, joins it into the commit before it with or
// without its message, or leaves it out.
public enum RewriteAction
{
    Pick,
    Reword,
    Squash,
    Fixup,
    Drop
}
