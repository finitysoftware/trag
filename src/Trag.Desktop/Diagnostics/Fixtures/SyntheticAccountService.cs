using Trag.Infrastructure.Accounts;
using Trag.Infrastructure.Platform.Secrets;

namespace Trag.Desktop.Diagnostics.Fixtures;

// An account service that keeps its secrets in memory and talks to the synthetic provider only.
internal static class SyntheticAccountService
{
    public static AccountService Create() => new(null, new MemorySecretStore(), new(new SyntheticProviderHandler()));
}
