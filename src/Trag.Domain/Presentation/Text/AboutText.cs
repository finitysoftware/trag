using Trag.Domain.Updates;

namespace Trag.Domain.Presentation.Text;

// The version line on the About page in Settings, and what the update check says beside it.
public static class AboutText
{
    // The window's title, which the title bar shows too.
    public const string WindowTitle = "Trag - Git Client";
    public const string CheckingForUpdates = "Checking for updates…";
    public const string UpToDate = "Trag is up to date.";
    public const string UpdateNotChecked = "Could not check for updates.";

    public static string Version(string informationalVersion) => "Version " + VersionNumber.Of(informationalVersion);
    public static string UpdateAvailable(string version) => "There is a new update! Version " + version;
}
