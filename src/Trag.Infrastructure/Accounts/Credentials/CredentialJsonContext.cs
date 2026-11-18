using System.Text.Json.Serialization;

namespace Trag.Infrastructure.Accounts.Credentials;

// A member left out or null is no request: it is refused as malformed JSON before anything reads it.
[JsonSourceGenerationOptions(RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(CredentialRequest))]
internal partial class CredentialJsonContext : JsonSerializerContext;
