namespace Trag.Domain.Presentation.Text;

// What the sidebar card, the keep dialog and the header say about a detached HEAD and the commits only it holds.
public sealed record DetachedHead(string Id, int Commits)
{
    public string ShortId => GitObjectId.Short(Id);
    public string CountText => Counted.Of(Commits, "commit") + (Commits == 1 ? " is" : " are");
    public string CardText => Commits == 0 ? "Not on a branch" : CountText + " not on a branch";
    public string KeepTitle => "Keep " + Counted.Of(Commits, "commit") + "?";
    public string Warning => "Detached HEAD at " + ShortId + (Commits == 0
        ? ". Commits made here are on no branch until you create one."
        : ". " + CountText + " on no branch yet; create a branch to keep them.");
}
