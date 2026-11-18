using Trag.Domain.Presentation.Text;

namespace Trag.Domain.Rebase;

// The rules of a rewrite's plan, over the commits in the order the plan gives them. A commit kept starts a group, and the
// squashed and fixed-up commits after it, dropped ones aside, join that group; the group becomes one commit. Something
// must stay, the first commit kept has to start a group, and a message the rewrite sets must say something.
public static class RewritePlan
{
    public static string? Refusal(IReadOnlyList<RewriteStep> steps)
    {
        var kept = steps.Where(step => step.Action != RewriteAction.Drop).ToArray();
        if (kept.Length == 0) return RewriteText.AllDropped;
        if (kept[0].IsFolded) return RewriteText.FirstFolded;
        return Groups(kept).Any(group => SetsMessage(group) && group[0].Message.Trim().Length == 0) ? RewriteText.EmptyMessage : null;
    }

    // The message a group proposes, as Git does: its first commit's, then each squashed commit's, a blank line apart.
    public static string DefaultMessage(IReadOnlyList<RewriteStep> steps, int head) =>
        string.Join("\n\n", GroupAt(steps, head).Where(step => step.Action != RewriteAction.Fixup).Select(step => step.Message.Trim()));

    // The commit whose message the step's group gets: a folded step joins the commit kept before it, past dropped ones,
    // and stands for itself when there is none; any other step is its own.
    public static int HeadOf(IReadOnlyList<RewriteStep> steps, int index)
    {
        if (!steps[index].IsFolded) return index;
        for (var head = index - 1; head >= 0; head--)
            if (steps[head].Action != RewriteAction.Drop && !steps[head].IsFolded) return head;
        return index;
    }

    // Whether a squash joins the group the head starts, which then gets a joined message; a dropped commit starts none.
    public static bool HasSquash(IReadOnlyList<RewriteStep> steps, int head) =>
        steps[head].Action != RewriteAction.Drop && GroupAt(steps, head).Any(step => step.Action == RewriteAction.Squash);

    // Nothing changes while every commit is picked in its own place with its own message.
    public static bool IsUnchanged(IReadOnlyList<RewriteStep> original, IReadOnlyList<RewriteStep> steps) =>
        steps.Select(step => step.Id).SequenceEqual(original.Select(step => step.Id))
        && steps.All(step => step.Action is RewriteAction.Pick or RewriteAction.Reword && step.Message == step.Original);

    // A group's message is set when its first commit's changed or a squash joins it; otherwise Git keeps the first one's.
    internal static bool SetsMessage(IReadOnlyList<RewriteStep> group) =>
        group[0].Message != group[0].Original || group.Any(step => step.Action == RewriteAction.Squash);

    private static IReadOnlyList<RewriteStep> GroupAt(IReadOnlyList<RewriteStep> steps, int head) =>
        Groups(steps.Skip(head).Where(step => step.Action != RewriteAction.Drop)).First();

    private static IEnumerable<IReadOnlyList<RewriteStep>> Groups(IEnumerable<RewriteStep> kept)
    {
        List<RewriteStep>? group = null;
        foreach (var step in kept)
        {
            if (group is not null && step.IsFolded) { group.Add(step); continue; }
            if (group is not null) yield return group;
            group = [step];
        }
        if (group is not null) yield return group;
    }
}
