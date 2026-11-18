namespace Trag.Domain.Naming;

// Git's own merge tools that run Vim or Neovim in a terminal, by the name Git knows them under: started from Trag they
// find no terminal to show in. Git itself does not tell them apart from the graphical ones.
public static class MergeTools
{
    private static readonly string[] Terminal = ["vimdiff", "vimdiff1", "vimdiff2", "vimdiff3", "nvimdiff", "nvimdiff1", "nvimdiff2", "nvimdiff3"];

    public static bool NeedsTerminal(string tool) => Terminal.Contains(tool);
}
