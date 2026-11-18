using Trag.Domain.Presentation.Text;

namespace Trag.Domain.UnitTests.Presentation.Text;

// What a stash write says when it ends: an apply that leaves conflicts points to where Trag resolves them.
public sealed class StashOutcomeTextTests
{
    [Fact]
    public void WhenApplyLeavesConflicts_ShouldPointToTragsConflictsView()
    {
        var text = StashOutcomeText.AppliedWithConflicts("stash@{0}");
        Assert.Contains("Conflicts", text);
        Assert.DoesNotContain("Git tools", text);
        Assert.Contains("the stash entry is kept", text);
    }
}
