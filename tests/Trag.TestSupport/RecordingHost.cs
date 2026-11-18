using Trag.ViewModels.Shell;

namespace Trag.TestSupport;

// The window's shell as the view models' tests see it: every request recorded, a folder revealed only when it is there,
// a file opened or revealed unless the test says it is missing, the save dialog answered with SaveAs and the folder dialog
// with Folder.
public sealed class RecordingHost : IShellHost
{
    public List<(string Request, string Value)> Calls { get; } = [];
    public List<Uri> Opened { get; } = [];
    public List<string> Revealed { get; } = [];
    public List<string> Terminals { get; } = [];
    public int Settings { get; private set; }
    public bool Missing { get; set; }
    public string? SaveAs { get; set; }
    public string? GitExecutable { get; set; }
    public string? Folder { get; set; }

    public bool Reveal(string path)
    {
        if (!Directory.Exists(path)) return false;
        Revealed.Add(path);
        return true;
    }

    public void OpenBrowser(Uri uri) => Opened.Add(uri);
    public void ShowSettings() => Settings++;
    public void OpenTerminal(string path) => Terminals.Add(path);
    public bool OpenFile(string path) => Record("open", path);
    public bool RevealFile(string path) => Record("reveal", path);
    public void CopyText(string text) => Calls.Add(("copy", text));

    public Task<string?> PickSaveFileAsync(string fileName)
    {
        Calls.Add(("save", fileName));
        return Task.FromResult(SaveAs);
    }

    public Task<string?> PickGitExecutableAsync()
    {
        Calls.Add(("git", ""));
        return Task.FromResult(GitExecutable);
    }

    public Task<string?> PickFolderAsync()
    {
        Calls.Add(("folder", ""));
        return Task.FromResult(Folder);
    }

    private bool Record(string request, string path)
    {
        if (Missing) return false;
        Calls.Add((request, path));
        return true;
    }
}
