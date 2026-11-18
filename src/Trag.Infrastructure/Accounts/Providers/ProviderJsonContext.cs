using System.Text.Json.Serialization;

namespace Trag.Infrastructure.Accounts.Providers;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OAuthResponse))]
[JsonSerializable(typeof(ProviderIdentity))]
[JsonSerializable(typeof(ProviderRepository))]
[JsonSerializable(typeof(ProviderRepository[]))]
[JsonSerializable(typeof(ProviderOwner[]))]
[JsonSerializable(typeof(RepositoryCreation))]
internal partial class ProviderJsonContext : JsonSerializerContext;
