using System.Diagnostics.CodeAnalysis;

namespace Trag.Domain.Naming;

// A name Trag accepts from outside and shows or sends back as it is: an account, a credential id, a provider's owner or
// repository, a folder kept in the settings. A blank, an over-long or a control-carrying value is not a name; how long
// one may be is the caller's limit.
public static class PlainNames
{
    public static bool IsValid([NotNullWhen(true)] string? value, int limit) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= limit && !value.Any(char.IsControl);
}
