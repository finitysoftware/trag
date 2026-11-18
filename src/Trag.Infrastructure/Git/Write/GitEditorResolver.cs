using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.Git.Write;

// Which editor opens a conflicted file, in Git's own order: GIT_EDITOR, then core.editor, then VISUAL, then EDITOR.
// An editor that needs a terminal is refused, because nothing would ever come back from it.
internal sealed class GitEditorResolver(WriteCommandRunner git, Func<string, string?>? environment = null)
{
    private readonly Func<string, string?> environment = environment ?? Environment.GetEnvironmentVariable;

    public async Task<(string? Editor, string? Rejection)> ResolveAsync(string root, CancellationToken ct)
    {
        var configured = await git.RunAsync(root, ["config", "--get", "core.editor"], ct).ConfigureAwait(false);
        if (configured.ExitCode is not (0 or 1)) return (null, ConflictOutcomeText.EditorUnreadable);
        var editor = environment("GIT_EDITOR")
            ?? (configured.ExitCode == 0 ? GitBytes.Text(configured.StandardOutput) : null)
            ?? environment("VISUAL") ?? environment("EDITOR");
        if (string.IsNullOrWhiteSpace(editor) || editor.Length > 8192 || editor.Contains('\0'))
            return (null, ConflictOutcomeText.EditorMissing);
        if (EditorCommands.NeedsTerminal(editor)) return (null, ConflictOutcomeText.EditorNeedsTerminal);
        return (editor, null);
    }
}
