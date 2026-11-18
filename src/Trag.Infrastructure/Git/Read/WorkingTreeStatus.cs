using Trag.Domain.Status;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Read;

// The one status command Trag reads a working tree with. The full read, the check before a discard and the read of the
// paths the watcher signaled ask the same question, so the arguments live in one place: no file-system monitor, every
// untracked file, no submodule recursion. Every untracked file is asked for as configuration, which also reaches the
// status Git runs inside each submodule without -u.
internal static class WorkingTreeStatus
{
    private static readonly string[] Status = ["-c", "core.fsmonitor=false", "-c", "status.showUntrackedFiles=all", "status", "--porcelain=v2", "-z", "--ignore-submodules=none"];

    public static async Task<StatusRecords> ReadAsync(PresetGitRunner git, string root, CancellationToken ct)
    {
        var records = StatusParser.Parse((await git.RunAsync(root, [.. Status, "--branch", "--no-ahead-behind"], ct).ConfigureAwait(false)).StandardOutput);
        return records with { Files = CheckedOut(root, records.Files) };
    }

    // Only what the pathspecs match, without the branch header; each pathspec carries its own magic.
    public static async Task<IReadOnlyList<FileStatus>> PathsAsync(PresetGitRunner git, string root, string[] pathspecs, CancellationToken ct) =>
        CheckedOut(root, StatusParser.Parse((await git.RunAsync(root, [.. Status, "--", .. pathspecs], ct, literalPathspecs: false).ConfigureAwait(false)).StandardOutput).Files);

    // Status gives a submodule never checked out the fields of one that is; only a checked-out one has its .git in its folder.
    private static IReadOnlyList<FileStatus> CheckedOut(string root, IReadOnlyList<FileStatus> files) =>
        [.. files.Select(file => file.IsSubmodule && Path.Exists(Path.Combine(root, file.Path.Display, ".git")) ? file with { SubmoduleIsCheckedOut = true } : file)];
}
