namespace Trag.Domain.Rebase;

// One commit of a rewrite, where the plan puts it: its id and subject, its message, what happens to it, and the message
// it gets when it starts a commit of the result (a new one for a reword, the joined one for a group with a squash).
public sealed record RewriteStep(string Id, string Subject, string Original, RewriteAction Action, string Message)
{
    public bool IsFolded => Action is RewriteAction.Squash or RewriteAction.Fixup;
}
