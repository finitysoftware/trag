using Trag.Domain.Naming;

namespace Trag.Infrastructure.Platform.Secrets;

// The account id every credential store is asked for. Both OS stores refuse the same ids, because the id ends up in a
// target name or in a lookup attribute where a blank, an over-long or a control-carrying value has no meaning.
internal static class SecretId
{
    private const string Invalid = "Invalid account ID.";

    public static void Validate(string id)
    {
        if (!PlainNames.IsValid(id, 200)) throw new ArgumentException(Invalid);
    }
}
