namespace Trag.Domain.Rebase;

// One commit of a rebase's plan as Git wrote it: the command (pick, edit, squash…), the commit and its subject.
public sealed record RebaseCommit(string Action, string Id, string Subject)
{
    public string ShortId => GitObjectId.Short(Id);
}
