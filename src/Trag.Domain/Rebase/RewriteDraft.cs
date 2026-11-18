using Trag.Domain.Presentation.Text;

namespace Trag.Domain.Rebase;

// The commits of a rewrite as they are edited, in the plan's order: what happens to each, a reword's own message (kept
// while the commit stays a reword, and again when it becomes one), and the joined message of a group with a squash
// once it is edited. A move starts every joined message afresh, a change of what a commit does the one of its group.
// Each change gives a new draft; Plan is what Git gets.
public sealed class RewriteDraft
{
    private readonly IReadOnlyList<RewriteStep> original;
    // Each commit with its action and the message a reword gives it.
    private readonly RewriteStep[] commits;
    private readonly string?[] joined;

    private RewriteDraft(IReadOnlyList<RewriteStep> original, RewriteStep[] commits, string?[] joined) =>
        (this.original, this.commits, this.joined) = (original, commits, joined);

    public static RewriteDraft Of(IReadOnlyList<RewriteStep> commits) => new(commits, [.. commits], new string?[commits.Count]);

    public IReadOnlyList<RewriteStep> Commits => commits;

    // Each commit with the message it gets: a group with a squash the joined one, edited or else proposed.
    public IReadOnlyList<RewriteStep> Plan
    {
        get
        {
            var own = Own();
            return [.. own.Select((step, index) => step with { Message = Final(own, index) })];
        }
    }

    // Why the plan cannot be asked, in RewriteText's words; empty when it can.
    public string Refusal
    {
        get
        {
            var plan = Plan;
            return RewritePlan.Refusal(plan) ?? (RewritePlan.IsUnchanged(original, plan) ? RewriteText.Unchanged : "");
        }
    }

    // The message the commit's group becomes, editable where the rewrite sets one: a reword's, or a squash's joined one.
    public bool CanEditMessage(int index)
    {
        var own = Own();
        var head = RewritePlan.HeadOf(own, index);
        return RewritePlan.HasSquash(own, head) || own[head].Action == RewriteAction.Reword;
    }

    public string MessageAt(int index)
    {
        var own = Own();
        return Final(own, RewritePlan.HeadOf(own, index));
    }

    public RewriteDraft WithMessage(int index, string message)
    {
        if (!CanEditMessage(index)) return this;
        var own = Own();
        var head = RewritePlan.HeadOf(own, index);
        if (RewritePlan.HasSquash(own, head)) return new(original, commits, Replaced(joined, head, message));
        return new(original, Replaced(commits, head, commits[head] with { Message = message }), joined);
    }

    // The group the commit leaves and the one it joins both start their joined message afresh.
    public RewriteDraft With(int index, RewriteAction action)
    {
        var changed = Replaced(commits, index, commits[index] with { Action = action });
        return new(original, changed, Replaced(Replaced(joined, RewritePlan.HeadOf(commits, index), null), RewritePlan.HeadOf(changed, index), null));
    }

    public RewriteDraft Moved(int from, int to)
    {
        var moved = commits.ToList();
        var commit = moved[from];
        moved.RemoveAt(from);
        moved.Insert(to, commit);
        return new(original, [.. moved], new string?[commits.Length]);
    }

    // Each commit with its own message: a reword's new one, any other its original.
    private RewriteStep[] Own() =>
        [.. commits.Select(step => step.Action == RewriteAction.Reword ? step : step with { Message = step.Original })];

    private string Final(IReadOnlyList<RewriteStep> own, int head) =>
        RewritePlan.HasSquash(own, head) ? joined[head] ?? RewritePlan.DefaultMessage(own, head) : own[head].Message;

    private static T[] Replaced<T>(T[] items, int index, T item)
    {
        var copy = (T[])items.Clone();
        copy[index] = item;
        return copy;
    }
}
