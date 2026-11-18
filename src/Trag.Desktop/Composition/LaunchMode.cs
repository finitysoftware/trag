namespace Trag.Desktop.Composition;

// The launch vocabulary, read once by ModeSelector. IsProbeMode is any of the three UI switches: settings come from
// TRAG_PROBE_SETTINGS, secrets stay in memory, accounts are not wired into the catalog and nothing is restored on open.
public sealed record LaunchMode(LaunchKind Kind, bool IsProbeMode, bool AttachUiProbe,
    bool RunUiSmoke, bool RunUiAuthSmoke, string? ProbeSettingsPath);
