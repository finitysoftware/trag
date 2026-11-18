using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;

namespace Trag.Domain.UnitTests.Presentation.Text;

// What a repository's read says when there is no Git to run: what to install, or that a git.exe can be chosen in
// Settings, since Trag finds Git again without a restart.
public sealed class SessionTextTests
{
    [Fact]
    public void WhenThereIsNoGitToRun_ShouldPointToSettingsNotToARestart()
    {
        Assert.Equal("Git could not be found. Install Git 2.43 or newer, or choose the git.exe you have in Settings → Git.",
            SessionText.Error(RepositoryError.GitMissing));
        Assert.Equal("This Git version is too old. Install Git 2.43 or newer, or choose a newer git.exe in Settings → Git.",
            SessionText.Error(RepositoryError.GitTooOld));
    }
}
