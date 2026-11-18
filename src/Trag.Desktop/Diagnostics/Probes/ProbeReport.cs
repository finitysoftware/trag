using System.Text.Json;

namespace Trag.Desktop.Diagnostics.Probes;

// One JSON line per probe outcome on stdout. Never logs external output, prompts, URLs, secrets or exception messages.
internal static class ProbeReport
{
    public static void Write(string probe, string result) =>
        Console.WriteLine(JsonSerializer.Serialize(new ProbeMessage(probe, result), ProbeJsonContext.Default.ProbeMessage));
}
