using System.Text.Json.Serialization;

namespace Trag.Infrastructure.Updates;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(UpdateResponse))]
internal partial class UpdateJsonContext : JsonSerializerContext;
