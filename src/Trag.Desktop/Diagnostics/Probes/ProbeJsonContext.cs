using System.Text.Json.Serialization;

namespace Trag.Desktop.Diagnostics.Probes;

[JsonSerializable(typeof(ProbeMessage))]
internal partial class ProbeJsonContext : JsonSerializerContext;
