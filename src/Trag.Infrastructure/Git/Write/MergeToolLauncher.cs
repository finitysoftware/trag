using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Write;

// Opens the user's own editor or merge tool on one conflicted file and waits for it, then verifies that the repository
// is still what it was. Nothing is staged here: the user reviews the working file and stages the resolution explicitly.
internal sealed class MergeToolLauncher(WriteCommandRunner git, GitEditorResolver editors, ExpectedStateCheck snapshots)
{
    public async Task<GitWriteResult> OpenMergeToolAsync(MergeToolRequest request, RepositorySnapshot before, IndexTransaction transaction, CancellationToken ct)
    {
        var path = request.Path.Display;
        var fullPath = RepositoryPathGuard.SafePath(before.Root, path);
        if (RepositoryPathGuard.HasLink(before.Root, fullPath)) return GitWriteResult.Rejected(ConflictOutcomeText.ToolOnLink);
        if (!File.Exists(fullPath)) return GitWriteResult.Rejected(ConflictOutcomeText.ToolWithoutFile);
        var (args, rejection) = request.Editor
            ? await EditorArgumentsAsync(before.Root, fullPath, ct).ConfigureAwait(false)
            : await ToolArgumentsAsync(before.Root, path, ct).ConfigureAwait(false);
        if (args is null) return GitWriteResult.Rejected(rejection!);
        return await VerifiedGitOperation.RunAsync(
            token => git.RunAsync(before.Root, args, token, transaction),
            (command, failure, token) => VerifyAsync(before, command, failure, token),
            (_, _) => new(GitWriteOutcome.Uncertain, ConflictOutcomeText.ToolNotVerified),
            TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
    }

    // Git editor configuration is a trusted shell command. Git's shell alias passes the raw file name as a positional
    // argument; no file name enters shell source.
    private async Task<(string[]? Arguments, string? Rejection)> EditorArgumentsAsync(string root, string fullPath, CancellationToken ct)
    {
        var (editor, rejection) = await editors.ResolveAsync(root, ct).ConfigureAwait(false);
        if (editor is null) return (null, rejection);
        return (["-c", "alias.trag-conflict-editor=!f() { " + editor + " \"$@\"; }; f", "trag-conflict-editor", fullPath], null);
    }

    // mergetool stages automatically. Its private index is deliberately discarded: the user reviews the working file
    // and explicitly stages the resolution in Trag.
    private async Task<(string[]? Arguments, string? Rejection)> ToolArgumentsAsync(string root, string path, CancellationToken ct)
    {
        var configured = await git.RunAsync(root, ["config", "--get", "merge.guitool"], ct).ConfigureAwait(false);
        if (configured.ExitCode == 1) configured = await git.RunAsync(root, ["config", "--get", "merge.tool"], ct).ConfigureAwait(false);
        var tool = GitBytes.Text(configured.StandardOutput);
        if (configured.ExitCode != 0 || tool.Length is 0 or > 128 || !tool.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            return (null, ConflictOutcomeText.ToolNotConfigured);
        if (MergeTools.NeedsTerminal(tool)) return (null, ConflictOutcomeText.ToolNeedsTerminal);
        return (["-c", "mergetool." + tool + ".trustExitCode=true", "mergetool", "--gui", "--tool=" + tool, "--no-prompt", "--", path], null);
    }

    private async Task<GitWriteResult> VerifyAsync(RepositorySnapshot before, GitResult? command, Exception? failure, CancellationToken ct)
    {
        if (!await snapshots.StillExpectedAsync(before, ct).ConfigureAwait(false))
            return new(GitWriteOutcome.Uncertain, ConflictOutcomeText.ToolStateChanged);
        var details = GitOutputText.Details(command);
        if (failure is not null) return new(GitWriteOutcome.Cancelled, ConflictOutcomeText.ToolInterrupted, details);
        if (command?.ExitCode == 0) return new(GitWriteOutcome.Succeeded, ConflictOutcomeText.ToolReturned, details);
        return GitWriteResult.Rejected(ConflictOutcomeText.ToolFailed, details);
    }
}
