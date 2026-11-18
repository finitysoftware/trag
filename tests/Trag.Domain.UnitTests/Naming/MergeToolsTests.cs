using Trag.Domain.Naming;

namespace Trag.Domain.UnitTests.Naming;

// Which of Git's own merge tools Trag refuses as terminal tools, by the name merge.guitool or merge.tool gives: Vim
// and Neovim in every layout are refused; gVim and the graphical tools are not, and Emacs' Emerge is left as it was,
// since whether it needs a terminal was not verified.
public sealed class MergeToolsTests
{
    [Theory]
    [InlineData("vimdiff", true)]
    [InlineData("vimdiff1", true)]
    [InlineData("vimdiff2", true)]
    [InlineData("vimdiff3", true)]
    [InlineData("nvimdiff", true)]
    [InlineData("nvimdiff1", true)]
    [InlineData("nvimdiff2", true)]
    [InlineData("nvimdiff3", true)]
    [InlineData("gvimdiff", false)]
    [InlineData("emerge", false)]
    [InlineData("meld", false)]
    public void WhenAMergeToolIsConfigured_ShouldTellWhetherItNeedsATerminal(string tool, bool terminal) =>
        Assert.Equal(terminal, MergeTools.NeedsTerminal(tool));
}
