namespace Trag.Domain.Presentation.Text;

// What the window says when it refuses to close, and when the system could not start what it was asked for.
public static class ShellText
{
    public const string OperationRunning = "A Git operation is running. Wait for its result, or press Cancel and wait for verification before closing.";
    public const string TerminalNotStarted = "No terminal could be started.";
    public const string FileManagerNotStarted = "The file manager could not be started.";
    public const string FileNotOpened = "No program could be started to open this file.";
    public const string BrowserNotStarted = "The browser could not be started.";
}
