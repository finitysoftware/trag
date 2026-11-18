using Trag.Desktop.Diagnostics.Probes;

namespace Trag.Desktop.UnitTests.Diagnostics;

public sealed class Phase0ProbesTests
{
    // ModeSelector never lets a mistyped probe reach the desktop UI.
    [Fact]
    public async Task WhenAProbeIsUnknown_ShouldAnswerUsageWithExitCodeTwo() => Assert.Equal(2, await Phase0Probes.RunAsync(["--diff-probe"]));
}
