using System.Text;
using static Trag.IntegrationTests.Git.Sync.SyncFixture;
using static Trag.IntegrationTests.Git.Write.MergeScenario;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.IntegrationTests.Git.Sync;
using Trag.Infrastructure.Git.Read;

namespace Trag.IntegrationTests.Git.Write;

// Resolving a conflicted pull: what the editor and the merge tool leave in the index, what staging a resolution and
// finishing the merge do, and that a conflict path is data, never a command.
public sealed class MergeResolutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenAContentOrDeleteConflictIsReadAfterARestart_ShouldStageFinishAndPushItsResolution(bool delete)
    {
        using var f = await Conflict(delete);
        var before = await f.Reader.ReadAsync(f.One, f.Token);
        Assert.True(before.HasSupportedMerge); Assert.Single(before.Files, file => file.IsConflict);
        var preview = await new CliFileDiffReader(new()).ReadAsync(f.One, before.Files.Single(file => file.IsConflict), false, false, f.Token);
        Assert.Contains("Conflict", preview.Message, StringComparison.OrdinalIgnoreCase);
        if (delete) File.Delete(Path.Combine(f.One, "shared.txt"));
        else await File.WriteAllTextAsync(Path.Combine(f.One, "shared.txt"), "reviewed resolution\n", f.Token);
        Assert.Equal(GitWriteOutcome.Succeeded, (await StageResolution(f, "shared.txt")).Outcome);
        Assert.DoesNotContain((await f.Reader.ReadAsync(f.One, f.Token)).Files, file => file.IsConflict);
        var result = await FinishMerge(f, "Reviewed merge\n\nBoth branches retained.");
        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(before.Head + " " + before.Merge!.IncomingHead, await f.Git(f.One, "show", "-s", "--format=%P", "HEAD"));
        Assert.Equal(RepositoryOperation.None, result.Snapshot!.Operation); Assert.Empty(result.Snapshot.Files);
        Assert.Equal(GitWriteOutcome.Succeeded, (await f.Execute(SyncAction.Push)).Outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenTheEditorOrTheMergeToolResolvesAFile_ShouldKeepTheIndexUnmergedUntilItIsStaged(bool mergetool)
    {
        using var f = await Conflict();
        var before = await f.Reader.ReadAsync(f.One, f.Token);
        var script = Path.Combine(f.Root, "edit conflict.sh");
        await File.WriteAllTextAsync(script, "#!/bin/sh\nprintf 'resolved by configured tool\\n' > \"$1\"\n", new UTF8Encoding(false), f.Token);
        if (mergetool)
        {
            await f.Git(f.One, "config", "merge.guitool", "tragfixture");
            await f.Git(f.One, "config", "mergetool.tragfixture.cmd", "sh " + Quote(script) + " \"$MERGED\"");
        }
        else await f.Git(f.One, "config", "core.editor", "sh " + Quote(script));
        var result = await MergeScenario.Write(f, expected => new MergeToolRequest(expected, before.Files.Single(file => file.IsConflict).Path, Editor: !mergetool));
        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(before.IndexFingerprint, result.Snapshot!.IndexFingerprint); Assert.Contains(result.Snapshot.Files, file => file.IsConflict);
        Assert.Contains("resolved by configured tool", await File.ReadAllTextAsync(Path.Combine(f.One, "shared.txt"), f.Token));
    }

    // core.editor is a shell command Git runs as it is written, a shell function included.
    [Fact]
    public async Task WhenCoreEditorIsAShellFunction_ShouldRunItAsConfigured()
    {
        using var f = await Conflict();
        await f.Git(f.One, "config", "core.editor", "edit() { printf 'resolved by a function\\n' > \"$1\"; }; edit");

        var result = await OpenEditor(f, "shared.txt");

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("resolved by a function\n", await File.ReadAllTextAsync(Path.Combine(f.One, "shared.txt"), f.Token));
    }

    // An editor path with spaces has to be quoted, for Trag as for Git; unquoted, the editor does not start, Trag says
    // so, and the conflict stays as it was.
    [Fact]
    public async Task WhenTheEditorPathHasSpacesUnquoted_ShouldSayTheEditorFailedAndKeepTheConflict()
    {
        using var f = await Conflict();
        var folder = Directory.CreateDirectory(Path.Combine(f.Root, "my tools")).FullName;
        var script = Path.Combine(folder, "edit.sh");
        await File.WriteAllTextAsync(script, "#!/bin/sh\nprintf 'resolved\\n' > \"$1\"\n", new UTF8Encoding(false), f.Token);
        await f.Git(f.One, "config", "core.editor", script.Replace('\\', '/'));
        var before = await File.ReadAllTextAsync(Path.Combine(f.One, "shared.txt"), f.Token);

        var result = await OpenEditor(f, "shared.txt");

        Assert.Equal((GitWriteOutcome.Rejected, ConflictOutcomeText.ToolFailed), (result.Outcome, result.Message));
        Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(f.One, "shared.txt"), f.Token));
    }

    // Vim and Neovim, Git's terminal merge tools, find no terminal in Trag: they are refused before Git runs, as a terminal
    // editor is, and the conflict stays as it was.
    [Fact]
    public async Task WhenMergeToolIsATerminalTool_ShouldRefuseItLikeATerminalEditor()
    {
        using var f = await Conflict();
        await f.Git(f.One, "config", "merge.tool", "vimdiff");
        var before = await File.ReadAllTextAsync(Path.Combine(f.One, "shared.txt"), f.Token);

        var result = await MergeScenario.Write(f, expected => new MergeToolRequest(expected, new(Encoding.UTF8.GetBytes("shared.txt")), Editor: false));

        Assert.Equal((GitWriteOutcome.Rejected, ConflictOutcomeText.ToolNeedsTerminal), (result.Outcome, result.Message));
        Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(f.One, "shared.txt"), f.Token));
    }

    // Something else moved the branch while the editor was open: the edit is the user's, and the outcome asks for a
    // review instead of claiming the editor simply returned.
    [Fact]
    public async Task WhenTheRepositoryChangesWhileTheEditorIsOpen_ShouldReportToolStateChanged()
    {
        using var f = await Conflict();
        var script = Path.Combine(f.Root, "edit and move.sh");
        await File.WriteAllTextAsync(script, "#!/bin/sh\nprintf 'resolved\\n' > \"$1\"\ngit update-ref refs/heads/trunk HEAD~1\n", new UTF8Encoding(false), f.Token);
        await f.Git(f.One, "config", "core.editor", "sh " + Quote(script));

        var result = await OpenEditor(f, "shared.txt");

        Assert.Equal((GitWriteOutcome.Uncertain, ConflictOutcomeText.ToolStateChanged), (result.Outcome, result.Message));
    }

    // Pinned: git mergetool keeps a backup of the conflicted file unless mergetool.keepBackup says otherwise, and Trag
    // leaves that setting to the user, so the backup shows as an untracked file.
    [Fact]
    public async Task WhenMergetoolKeepsBackups_ShouldLeaveTheOrigFileAsUntracked()
    {
        using var f = await Conflict();
        var script = Path.Combine(f.Root, "tool.sh");
        await File.WriteAllTextAsync(script, "#!/bin/sh\nprintf 'resolved by tool\\n' > \"$1\"\n", new UTF8Encoding(false), f.Token);
        await f.Git(f.One, "config", "merge.guitool", "tragfixture");
        await f.Git(f.One, "config", "mergetool.tragfixture.cmd", "sh " + Quote(script) + " \"$MERGED\"");

        var result = await MergeScenario.Write(f, expected => new MergeToolRequest(expected, new(Encoding.UTF8.GetBytes("shared.txt")), Editor: false));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Contains(result.Snapshot!.Files, file => file.Path.Display == "shared.txt.orig" && file.IsUntracked);
    }

    [Fact]
    public async Task WhenAHookRefusesTheMergeCommitOnFinish_ShouldKeepTheMessageAndTheStagedResolutionForARetry()
    {
        using var f = await Conflict();
        await File.WriteAllTextAsync(Path.Combine(f.One, "shared.txt"), "resolved\n", f.Token);
        await StageResolution(f, "shared.txt");
        var before = await f.Reader.ReadAsync(f.One, f.Token);
        await f.Hook(f.One, "commit-msg", "echo 'fixture merge message rejected' >&2\nexit 1\n");
        var result = await FinishMerge(f, "Keep this message");
        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome); Assert.Contains("fixture merge message rejected", result.Details);
        Assert.Equal(before.Head, result.Snapshot!.Head); Assert.Equal(before.IndexFingerprint, result.Snapshot.IndexFingerprint);
        Assert.Equal(before.Merge, result.Snapshot.Merge);
        await f.Hook(f.One, "commit-msg", "exit 0\n");
        Assert.Equal(GitWriteOutcome.Succeeded, (await FinishMerge(f, "Keep this message")).Outcome);
    }

    [Fact]
    public async Task WhenAConflictPathLooksLikeAShellCommandOrPathspec_ShouldTreatItAsALiteralPath()
    {
        using var f = await SyncFixture.Create(); const string path = "-č $(echo injected) ' [file].txt";
        await f.Git(f.One, "mv", "--", "shared.txt", path); await f.Git(f.One, "commit", "-m", "unusual path");
        await f.Git(f.One, "push", "origin", "HEAD"); await f.Git(f.Two, "pull", "--ff-only");
        foreach (var root in new[] { f.One, f.Two })
        { await File.AppendAllTextAsync(Path.Combine(root, path), root == f.One ? "local\n" : "remote\n", f.Token); await f.Git(root, "add", "--", path); await f.Git(root, "commit", "-m", "diverge"); }
        await f.Git(f.Two, "push", "origin", "HEAD"); Assert.Equal(GitWriteOutcome.NeedsResolution, (await f.Execute(SyncAction.Pull)).Outcome);
        var script = Path.Combine(f.Root, "literal editor.sh");
        await File.WriteAllTextAsync(script, "#!/bin/sh\nprintf 'literal path resolution\\n' > \"$1\"\n", new UTF8Encoding(false), f.Token);
        await f.Git(f.One, "config", "core.editor", "sh " + Quote(script));
        Assert.Equal(GitWriteOutcome.Succeeded, (await OpenEditor(f, path)).Outcome);
        Assert.Equal("literal path resolution\n", await File.ReadAllTextAsync(Path.Combine(f.One, path), f.Token));
        Assert.Equal(GitWriteOutcome.Succeeded, (await StageResolution(f, path)).Outcome);
        Assert.DoesNotContain((await f.Reader.ReadAsync(f.One, f.Token)).Files, file => file.IsConflict);
    }

    // Neither merge.guitool nor merge.tool: nothing is started, and the user is told what to configure.
    [Fact]
    public async Task WhenNoMergeToolIsConfigured_ShouldSayWhatToConfigure()
    {
        using var f = await Conflict();

        var result = await MergeScenario.Write(f, expected => new MergeToolRequest(expected, new(Encoding.UTF8.GetBytes("shared.txt")), Editor: false));

        Assert.Equal((GitWriteOutcome.Rejected, ConflictOutcomeText.ToolNotConfigured), (result.Outcome, result.Message));
    }

    // merge.guitool comes before merge.tool, as for git mergetool --gui; a tool's name is a name of at most 128 letters,
    // digits, '-' and '_', so a longer one is refused as not configured.
    [Theory]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public async Task WhenBothToolsAreConfigured_ShouldOpenTheGuiToolUpToANameOf128Characters(int length, bool opens)
    {
        using var f = await Conflict();
        var name = new string('t', length);
        foreach (var (key, tool, text) in new[] { ("merge.tool", "plaintool", "plain tool"), ("merge.guitool", name, "gui tool") })
        {
            var script = Path.Combine(f.Root, tool[..Math.Min(tool.Length, 20)] + ".sh");
            await File.WriteAllTextAsync(script, "#!/bin/sh\nprintf '" + text + "\\n' > \"$1\"\n", new UTF8Encoding(false), f.Token);
            await f.Git(f.One, "config", key, tool);
            await f.Git(f.One, "config", "mergetool." + tool + ".cmd", "sh " + Quote(script) + " \"$MERGED\"");
        }

        var result = await MergeScenario.Write(f, expected => new MergeToolRequest(expected, new(Encoding.UTF8.GetBytes("shared.txt")), Editor: false));

        if (opens)
        {
            Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
            Assert.Equal("gui tool\n", await File.ReadAllTextAsync(Path.Combine(f.One, "shared.txt"), f.Token));
        }
        else Assert.Equal((GitWriteOutcome.Rejected, ConflictOutcomeText.ToolNotConfigured), (result.Outcome, result.Message));
    }

    // Both sides deleted the file a rename on each side left behind: there is no working file to open.
    [Fact]
    public async Task WhenTheConflictHasNoWorkingFile_ShouldNotOpenTheEditor()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.One, "mv", "shared.txt", "one.txt"); await f.Git(f.One, "commit", "-m", "one renames");
        await f.Git(f.Two, "mv", "shared.txt", "two.txt"); await f.Git(f.Two, "commit", "-m", "two renames"); await f.Git(f.Two, "push", "origin", "trunk");
        Assert.Equal(GitWriteOutcome.NeedsResolution, (await f.Execute(SyncAction.Pull)).Outcome);
        await f.Git(f.One, "config", "core.editor", "true");

        var result = await OpenEditor(f, "shared.txt");

        Assert.Equal((GitWriteOutcome.Rejected, ConflictOutcomeText.ToolWithoutFile), (result.Outcome, result.Message));
    }

    // Cancel while the editor is open stops it; what it wrote stays, and the conflict is still open.
    [Fact]
    public async Task WhenTheEditorIsCancelledWhileOpen_ShouldSayTheToolWasInterruptedAndKeepTheConflict()
    {
        using var f = await Conflict();
        var script = Path.Combine(f.Root, "slow editor.sh");
        await File.WriteAllTextAsync(script, "#!/bin/sh\nprintf 'half done\\n' > \"$1\"\n: > \"$(dirname \"$0\")/started\"\nsleep 60\n", new UTF8Encoding(false), f.Token);
        await f.Git(f.One, "config", "core.editor", "sh " + Quote(script));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(f.Token);

        var writing = MergeScenario.Write(f, expected => new MergeToolRequest(expected, new(Encoding.UTF8.GetBytes("shared.txt")), Editor: true), cancel.Token);
        await WaitFor(Path.Combine(f.Root, "started"), f.Token);
        await cancel.CancelAsync();
        var result = await writing;

        Assert.Equal((GitWriteOutcome.Cancelled, ConflictOutcomeText.ToolInterrupted), (result.Outcome, result.Message));
        Assert.Equal("half done\n", await File.ReadAllTextAsync(Path.Combine(f.One, "shared.txt"), f.Token));
        Assert.Contains(result.Snapshot!.Files, file => file.IsConflict);
    }
}
