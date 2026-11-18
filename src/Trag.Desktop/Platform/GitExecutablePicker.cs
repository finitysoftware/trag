using Avalonia.Platform.Storage;
using Trag.Domain.Presentation.Text;

namespace Trag.Desktop.Platform;

// The file dialog that asks for the git.exe Trag should run; null when it is closed without a choice.
public static class GitExecutablePicker
{
    public static async Task<string?> PickAsync(IStorageProvider storage)
    {
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = GitSetupText.ChooseTitle, AllowMultiple = false,
            FileTypeFilter = [new(GitSetupText.ChooseFilter) { Patterns = [OperatingSystem.IsWindows() ? "git.exe" : "git"] }]
        });
        var path = files.Count == 0 ? null : files[0].TryGetLocalPath();
        foreach (var file in files) file.Dispose();
        return path;
    }
}
