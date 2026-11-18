using Trag.Domain.Rebase;

namespace Trag.Domain.Presentation.Text;

// One choice of what happens to a commit, as the edit dialog offers it: the action, its name and what it does.
public sealed record RewriteChoice(RewriteAction Action, string Name, string Hint);
