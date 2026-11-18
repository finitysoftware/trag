using Trag.Domain.Presentation.Text;

namespace Trag.Domain.UnitTests.Presentation.Text;

public sealed class SubmoduleTextTests
{
    // The notice names the submodules and says what each way out does: update goes back to the recorded commits, staging
    // keeps the new one.
    [Fact]
    public void WhenSubmodulesAreNotAtTheRecordedCommits_ShouldNameThemAndSayWhatEachChoiceDoes()
    {
        Assert.Equal("Submodule sub is not at the commit this branch records. Update checks that commit out; staging sub keeps its new commit instead.",
            SubmoduleText.NotAtRecorded(["sub"]));
        Assert.Equal("2 submodules are not at the commits this branch records: lib, sub. Update checks those commits out; staging one keeps its new commit instead.",
            SubmoduleText.NotAtRecorded(["lib", "sub"]));
    }
}
