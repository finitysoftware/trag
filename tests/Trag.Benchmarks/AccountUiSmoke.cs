using System.Security.Cryptography;
using System.Text.Json;

namespace Trag.Benchmarks;

internal static class AccountUiSmoke
{
    public static async Task RunAsync(string executable, string output, CancellationToken ct)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var token = timeout.Token; var transcript = new List<string>();
        await using var ui = new ProbeSession(executable, Path.Combine(output, "settings.json"), transcript, token);
        Task Shot(string name) => ui.Shot(Path.Combine(output, name + ".png"), 150);
        await ui.Command("theme dark", "TRAG_THEME_OK");
        await ui.Command("settings-open", "TRAG_SETTINGS_OK"); await Shot("settings-dark");
        await ui.Command("settings-fixture", "TRAG_SETTINGS_OK"); await Shot("accounts-dark");
        await ui.Command("settings-close", "TRAG_SETTINGS_OK");
        await ui.Command("theme light", "TRAG_THEME_OK");
        await ui.Command("settings-open", "TRAG_SETTINGS_OK"); await Shot("settings-light");
        await ui.Command("settings-gitlab", "TRAG_SETTINGS_OK"); await Shot("gitlab-entry-light");
        await ui.Command("settings-close", "TRAG_SETTINGS_OK");
        for (var i = 0; i < 4; i++) { await ui.Command("settings-open", "TRAG_SETTINGS_OK"); await ui.Command("settings-close", "TRAG_SETTINGS_OK"); }
        await Task.Delay(TimeSpan.FromSeconds(30), token); var released = ui.Resident();
        if (await ui.QuitAsync() != 0) throw new InvalidOperationException("UI did not close cleanly.");
        await File.WriteAllLinesAsync(Path.Combine(output, "ui-transcript.log"), transcript, token);
        await File.WriteAllTextAsync(Path.Combine(output, "ui-errors.log"), await ui.Errors, token);
        await File.WriteAllTextAsync(Path.Combine(output, "accounts-smoke.json"), JsonSerializer.Serialize(new
        { Executable = Path.GetFullPath(executable), Sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executable, token))),
            ThemeChecks = 2, ReleasedAfterSixSettingsSessionsBytes = released, MemoryBudgetBytes = 100_000_000, MemoryBudgetPassed = released <= 100_000_000,
            SyntheticAccountsOnly = true }, new JsonSerializerOptions { WriteIndented = true }), token);
        Console.WriteLine("Native Settings and account views passed; released resident bytes: " + released);
    }
}
