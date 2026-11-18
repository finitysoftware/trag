using Trag.Desktop.Composition;

namespace Trag.Desktop.UnitTests.Composition;

public sealed class ModeSelectorTests
{
    private static Func<string, string?> Environment(params (string Name, string Value)[] variables) =>
        name => variables.FirstOrDefault(v => v.Name == name).Value;

    [Fact]
    public void WhenTheAskPassMarkerIsSet_ShouldLaunchAskPassWhateverTheArguments()
    {
        var mode = ModeSelector.Select(["--git-probe", "repo", "file"], Environment(("TRAG_ASKPASS", "1")));
        Assert.Equal(LaunchKind.AskPass, mode.Kind); Assert.False(mode.IsProbeMode);
    }

    [Theory]
    [InlineData("--git-probe")] [InlineData("--vault-probe")] [InlineData("--auth-probe")] [InlineData("--process-probe")] [InlineData("--account-probe")]
    public void WhenAProbeArgumentComesFirst_ShouldSelectTheCommandLineProbesAndOnlyThen(string probe)
    {
        var mode = ModeSelector.Select([probe], Environment());
        Assert.Equal(LaunchKind.Probe, mode.Kind); Assert.False(mode.IsProbeMode);
        Assert.Equal(LaunchKind.Ui, ModeSelector.Select(["repo", probe], Environment()).Kind);
    }

    [Fact]
    public void WhenTheFirstArgumentIsAnUnknownProbeOrAUiSwitch_ShouldSelectTheProbesOnlyForTheUnknownProbe()
    {
        Assert.Equal(LaunchKind.Probe, ModeSelector.Select(["--diff-probe"], Environment()).Kind);
        Assert.Equal(LaunchKind.Ui, ModeSelector.Select(["--ui-probe"], Environment()).Kind);
        Assert.Equal(LaunchKind.Ui, ModeSelector.Select(["--ui-smoke", "--git-probe"], Environment()).Kind);
    }

    [Fact]
    public void WhenTragLaunchesWithoutArguments_ShouldStartTheUiAndIgnoreTheProbeEnvironment()
    {
        var mode = ModeSelector.Select([], Environment(("TRAG_PROBE_SETTINGS", "ignored.json")));
        Assert.Equal(LaunchKind.Ui, mode.Kind);
        Assert.False(mode.IsProbeMode); Assert.False(mode.AttachUiProbe); Assert.False(mode.RunUiSmoke); Assert.False(mode.RunUiAuthSmoke);
        Assert.Null(mode.ProbeSettingsPath);
    }

    [Fact]
    public void WhenAUiSwitchIsGiven_ShouldTurnOnProbeModeAndReadTheProbeEnvironment()
    {
        var probe = ModeSelector.Select(["--ui-probe"], Environment(("TRAG_PROBE_SETTINGS", "probe/settings.json")));
        Assert.True(probe.IsProbeMode); Assert.True(probe.AttachUiProbe); Assert.False(probe.RunUiSmoke);
        Assert.Equal("probe/settings.json", probe.ProbeSettingsPath);
        var smoke = ModeSelector.Select(["--ui-smoke"], Environment());
        Assert.True(smoke.IsProbeMode); Assert.True(smoke.RunUiSmoke); Assert.False(smoke.RunUiAuthSmoke);
        var auth = ModeSelector.Select(["--ui-auth-smoke"], Environment());
        Assert.True(auth.IsProbeMode); Assert.True(auth.RunUiSmoke); Assert.True(auth.RunUiAuthSmoke);
    }
}
