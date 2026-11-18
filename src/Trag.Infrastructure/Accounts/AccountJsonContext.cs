using System.Text.Json.Serialization;
using Trag.Infrastructure.Accounts.Providers;

namespace Trag.Infrastructure.Accounts;

// A member left out or null is no account: the file is refused whole, as a malformed one is.
[JsonSourceGenerationOptions(RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(AccountDocument))]
[JsonSerializable(typeof(TokenData))]
internal partial class AccountJsonContext : JsonSerializerContext;
