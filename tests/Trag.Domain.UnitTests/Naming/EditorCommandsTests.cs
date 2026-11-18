using Trag.Domain.Naming;

namespace Trag.Domain.UnitTests.Naming;

// Which editors need a terminal, by the program their command line starts: a quoted path to its closing quote, or else
// the command to its first space.
public sealed class EditorCommandsTests
{
    [Theory]
    [InlineData("vim", true)]
    [InlineData("/usr/bin/nano", true)]
    [InlineData("C:\\tools\\vim.exe --wait", true)]
    [InlineData("\"C:\\Program Files\\Vim\\vim91\\vim.exe\"", true)]
    [InlineData("'C:\\Program Files\\Git\\usr\\bin\\nano.exe' -w", true)]
    [InlineData("\"C:\\tools\\vim.exe --wait", true)]
    [InlineData("code --wait", false)]
    [InlineData("\"C:\\Program Files\\Microsoft VS Code\\Code.exe\" --wait", false)]
    public void WhenAnEditorIsConfigured_ShouldTellWhetherItNeedsATerminal(string command, bool terminal) =>
        Assert.Equal(terminal, EditorCommands.NeedsTerminal(command));
}
