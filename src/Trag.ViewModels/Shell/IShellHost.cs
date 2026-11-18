namespace Trag.ViewModels.Shell;

// What the view models ask of the window they live in: folders and files shown or opened, a page in the browser, a
// terminal, Settings, the clipboard and the file and folder dialogs. The window attaches its host when it takes the shell and detaches
// it when it lets go; without a host the requests go nowhere, as in a test.
public interface IShellHost
{
    // Shows the folder in the file manager; false when the folder is no longer there.
    bool Reveal(string path);
    // Opens an HTTPS page the view model has already judged safe.
    void OpenBrowser(Uri uri);
    void ShowSettings();
    // Opens a terminal in the folder.
    void OpenTerminal(string path);
    // Opens the file in the program the machine has for it; false when the file is not there.
    bool OpenFile(string path);
    // Shows the file, selected, in the file manager; false when the file is not there.
    bool RevealFile(string path);
    void CopyText(string text);
    // Asks where to save a file, offering the name; null when the dialog is closed without a choice.
    Task<string?> PickSaveFileAsync(string fileName);
    // Asks for the git.exe Trag should run; null when the dialog is closed without a choice.
    Task<string?> PickGitExecutableAsync();
    // Asks for a local folder; null when the dialog is closed without a choice. Throws when the window has no folder
    // dialog or the folder chosen is not a local one.
    Task<string?> PickFolderAsync();
}
