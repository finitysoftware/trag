namespace Trag.Domain.Rebase;

// What Trag hands Git for a rewrite: the plan for git-rebase-todo, and the messages its exec lines read, in order, from
// the files 1.txt, 2.txt… of the messages folder.
public sealed record RewriteScript(string Todo, IReadOnlyList<string> Messages);
