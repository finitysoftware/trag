using System.Text;
using Trag.Desktop.Dialogs.Settings;
using Trag.Desktop.Services;
using Trag.Infrastructure.Accounts;

namespace Trag.Desktop.Diagnostics.Fixtures;

// Signs two synthetic GitLab accounts into the settings window's service for the account screenshots.
internal static class SettingsFixture
{
    public static async Task PopulateAsync(SettingsWindow window, SettingsWindowHost host)
    {
        if (host.AccountService is not AccountService service) return;
        foreach (var (address, token) in new[] { ("https://gitlab.com", "fixture-alice"), ("https://git.example.invalid:8443/team", "fixture-bob") })
            await service.SignInGitLabAsync(address, Encoding.UTF8.GetBytes(token), true, CancellationToken.None);
        await window.Model.LoadAsync();
    }
}
