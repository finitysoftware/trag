using Trag.Domain.Presentation.Text;

namespace Trag.Domain.UnitTests.Presentation.Text;

public sealed class TransportTextTests
{
    // A push that records submodule commits no remote has names the submodules to push first.
    [Fact]
    public void WhenSubmoduleCommitsAreOnNoRemote_ShouldNameTheSubmodulesToPushFirst()
    {
        Assert.Equal("Push submodule sub first. This branch records a commit of it that none of its remotes has, so others could not fetch that commit.",
            TransportText.SubmodulesNotPushed(["sub"]));
        Assert.Equal("Push submodules lib, sub first. This branch records commits of them that none of their remotes has, so others could not fetch those commits.",
            TransportText.SubmodulesNotPushed(["lib", "sub"]));
    }
}
