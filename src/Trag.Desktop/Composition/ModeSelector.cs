using Trag.Infrastructure.Accounts.Credentials;

namespace Trag.Desktop.Composition;

// The only place that knows the launch switches: Git's askpass launch, a --<name>-probe first argument and the UI
// switches --ui-probe, --ui-smoke and --ui-auth-smoke, plus the probe settings (TRAG_PROBE_SETTINGS).
public static class ModeSelector
{
    public static LaunchMode Select(string[] args, Func<string, string?> environment)
    {
        var kind = AskPassClient.IsAskPassLaunch(environment) ? LaunchKind.AskPass : IsProbeCommand(args) ? LaunchKind.Probe : LaunchKind.Ui;
        var uiProbe = args.Contains("--ui-probe", StringComparer.Ordinal);
        var uiSmoke = args.Contains("--ui-smoke", StringComparer.Ordinal);
        var uiAuthSmoke = args.Contains("--ui-auth-smoke", StringComparer.Ordinal);
        var probeMode = uiProbe || uiSmoke || uiAuthSmoke;
        return new(kind, probeMode, uiProbe, uiSmoke || uiAuthSmoke, uiAuthSmoke,
            probeMode ? environment("TRAG_PROBE_SETTINGS") : null);
    }

    // A command-line probe names itself --<name>-probe in first position; Phase0Probes runs the ones it knows and
    // refuses the rest, so a mistyped probe never opens the desktop UI. --ui-probe is the UI's own control channel.
    private static bool IsProbeCommand(string[] args) =>
        args is [var first, ..] && first != "--ui-probe"
        && first.StartsWith("--", StringComparison.Ordinal) && first.EndsWith("-probe", StringComparison.Ordinal);
}
