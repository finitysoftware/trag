using System.Globalization;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Accounts;

namespace Trag.Infrastructure.Accounts.Providers;

// Who a token belongs to. The provider says where its own name field is; what a usable identity looks like is the
// same either way, because the account id is derived from it.
internal sealed class ProviderIdentityReader(ProviderHttpClient http)
{
    public async Task<(string Id, string UserName)> ReadAsync(Uri api, string token, Func<ProviderIdentity, string?> userName, CancellationToken ct)
    {
        using var request = ProviderHttpClient.Request(HttpMethod.Get, new Uri(api, "user"), token);
        var user = await http.JsonAsync(request, ProviderJsonContext.Default.ProviderIdentity,
            ProviderText.IdentityUnreadable, ct).ConfigureAwait(false);
        var name = user is null ? null : userName(user);
        if (user?.Id is null or <= 0 || !AccountName.IsValid(name))
            throw new AccountException(ProviderText.IdentityInvalid);
        return (user.Id.ToString(CultureInfo.InvariantCulture), name!);
    }
}
