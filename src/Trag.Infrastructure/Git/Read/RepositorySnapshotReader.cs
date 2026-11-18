using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using Trag.Application.Ports.Git;
using Trag.Domain;
using Trag.Domain.Errors;
using Trag.Domain.Merge;
using Trag.Domain.Status;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Read;

// One whole reading of a repository. Everything Git says is read around the same index fingerprint, HEAD, branch,
// operation and merge state: a snapshot is returned only when the repository did not move while it was being read,
// which is the same question a write asks before it publishes, answered by the same comparison.
internal sealed class RepositorySnapshotReader(PresetGitRunner git, HeadReader heads, GitIdentityReader identity, RepositoryLimitationScanner scanner) : IRepositoryReader
{
    private readonly MergeResolutionReader resolutions = new(git);
    private readonly ExpectedStateCheck comparison = new(new(heads, identity, git));
    // The Git program asked last, with its version: asked once per program, again when another one runs, as after one is
    // chosen in Settings; a missing Git still fails every read. A reference, so reads running side by side never see the
    // program of one answer with the version of another.
    private Tuple<string, string>? gitVersion;
    // Whether that program runs git-lfs, which a MinGit without it does not, asked the same way, once Git LFS is set up.
    private Tuple<string, bool>? gitLfs;

    // Filesystem checks, process setup and parsing also stay off the UI thread.
    public Task<RepositorySnapshot> ReadAsync(string path, CancellationToken ct) =>
        Task.Run(() => ReadBackgroundAsync(path, ct), ct);

    private async Task<RepositorySnapshot> ReadBackgroundAsync(string path, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) throw new RepositoryException(RepositoryError.PathUnavailable);
            // Git takes a folder it may not look into for no repository at all; listing it says that it cannot be read.
            _ = Directory.EnumerateFileSystemEntries(path).Any();
            path = Path.GetFullPath(path);
            if (gitVersion is not { } asked || asked.Item1 != git.Program)
                gitVersion = asked = Tuple.Create(git.Program, GitVersionPolicy.Require(GitBytes.StrictLine((await git.RunAsync(path, ["--version"], ct)).StandardOutput)).ToString());
            var version = asked.Item2;
            var (root, gitDir, commonDir, superproject) = await LocateAsync(path, ct);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var snapshot = await ReadAttemptAsync(root, gitDir, commonDir, version, ct);
                if (snapshot is not null) return snapshot with { Superproject = superproject };
            }
            throw new RepositoryException(RepositoryError.ChangedDuringRead);
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 2) { throw new RepositoryException(RepositoryError.GitMissing); }
        catch (GitOutputLimitException) { throw new RepositoryException(RepositoryError.OutputTooLarge); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException or Win32Exception or GitCommandException)
        { throw new RepositoryException(RepositoryError.ReadFailed); }
    }

    // One call says whether this is a repository with a working tree and where its root, Git directory and common
    // directory are. rev-parse prints the answers in argument order, so a bare repository has printed "true" by the time
    // it refuses the working tree question, and a folder that is no repository prints nothing; under
    // safe.bareRepository=explicit Git refuses a bare repository before it answers anything, in words of its own. Asking
    // for a superproject makes Git run another Git in the parent folder, so it is asked only where .git is a file, as in a
    // submodule.
    private async Task<(string Root, string GitDirectory, string CommonDirectory, string? Superproject)> LocateAsync(string path, CancellationToken ct)
    {
        var result = await git.ResultAsync(path, ["rev-parse", "--is-bare-repository", "--show-toplevel", "--absolute-git-dir", "--path-format=absolute", "--git-common-dir"], ct);
        var lines = GitBytes.StrictText(result.StandardOutput).Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        if (result.ExitCode != 0)
        {
            if (lines[0] == "true" || GitBareRepository.Refused(result)) throw new RepositoryException(RepositoryError.BareRepository);
            throw new RepositoryException(GitOwnership.Refused(result) ? RepositoryError.UnsafeDirectory : RepositoryError.NotRepository);
        }
        if (lines is not ["false", var root, var gitDir, var commonDir]) throw new FormatException("Invalid repository location.");
        if (!File.Exists(Path.Combine(root, ".git"))) return (root, gitDir, commonDir, null);
        var superproject = GitBytes.StrictText((await git.ResultAsync(root, ["rev-parse", "--show-superproject-working-tree"], ct)).StandardOutput).TrimEnd('\r');
        return (root, gitDir, commonDir, superproject.Length == 0 ? null : superproject);
    }

    // The index fingerprint, operation, merge and rebase state are read first, everything else after them, and then the
    // comparison reads HEAD, the branch and all of them again: a difference means the repository moved meanwhile.
    private async Task<RepositorySnapshot?> ReadAttemptAsync(string root, string gitDir, string commonDir, string version, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fingerprint = await IndexFingerprint.ReadAsync(gitDir, ct).ConfigureAwait(false);
        var roots = await RootRefs.ReadAsync(git, root, gitDir, commonDir, ct).ConfigureAwait(false);
        var operation = RepositoryOperationDetector.Detect(gitDir, roots);
        var merge = await MergeMetadataReader.ReadAsync(gitDir, roots, ct).ConfigureAwait(false);
        var rebase = await RebaseMetadataReader.ReadAsync(gitDir, roots, ct).ConfigureAwait(false);
        var pickHead = operation switch
        {
            RepositoryOperation.Revert => PickHead(roots["REVERT_HEAD"]),
            RepositoryOperation.CherryPick => PickHead(roots["CHERRY_PICK_HEAD"]),
            _ => null
        };
        var status = await WorkingTreeStatus.ReadAsync(git, root, ct);
        // Git makes a reword it replays before it asks for the message, so one a hook or an empty editor refused stops with
        // the commit made, as an edit stop does, but with no amend file. A reword stopped at a conflict and reset since
        // leaves the same files, so it is an edit stop only while HEAD is the stopped commit made anew; MERGE_MSG and a
        // conflict mark a reword stopped at its conflict without that read.
        if (rebase is { StoppedForEdit: false, Current.Action: "reword" or "r", Stopped: { } stopped } && !status.Files.Any(file => file.IsConflict)
            && !File.Exists(Path.Combine(gitDir, "MERGE_MSG")) && await IsMadeAnewAsync(root, stopped, ct))
            rebase = rebase with { StoppedForEdit = true };
        var (branch, head, upstream) = status.Header();
        // A rebase detaches HEAD while it replays commits; those commits are the rebase's, not commits left behind.
        var detachedCommits = operation == RepositoryOperation.Rebase ? 0 : await CountDetachedCommitsAsync(root, branch, head, ct);
        if (rebase is { Limitation.Length: 0, Onto.Length: > 0 }) rebase = rebase with { OntoName = await ReadOntoNameAsync(root, rebase.Onto, ct) };
        var refs = await ReadRefsAsync(root, upstream, ct);
        var config = await ReadConfigurationAsync(root, ct);
        var trackingRemote = branch is null ? null : config.Value($"branch.{branch}.remote");
        var mergeRef = branch is null ? null : config.Value($"branch.{branch}.merge");
        var remotes = await ReadRemotesAsync(root, config, ct);
        var lfsInstalled = config.LfsInstalled && await GitLfsRunsAsync(root, ct);
        var (limitations, lfsFiles) = await scanner.ScanAsync(root, gitDir, commonDir, fingerprint, config, lfsInstalled, ct);
        // An operation in progress also says what it has resolved so far and how much of each open conflict is left.
        var incoming = RepositoryOperations.Theirs(operation, merge?.IncomingHead, rebase?.Stopped, pickHead);
        var decided = incoming is null && !status.Files.Any(file => file.IsConflict) ? MergeResolutions.None
            : await resolutions.ReadAsync(root, incoming, status.Files, ct).ConfigureAwait(false);
        var author = await identity.ReadAsync(root, "GIT_AUTHOR_IDENT", ct);
        var committer = await identity.ReadAsync(root, "GIT_COMMITTER_IDENT", ct);
        var snapshot = new RepositorySnapshot(root, version, branch, head, upstream, trackingRemote, remotes,
            RepositoryOperations.With(operation, status.Files), limitations, status.Files)
        {
            GitDirectory = gitDir, CommonDirectory = commonDir, IndexFingerprint = fingerprint,
            Author = author, Committer = committer, Signing = config.Signing,
            SignsTags = config.Flag("tag.gpgsign"), SigningKey = CommitSignings.Key(config.Value("gpg.format")), UpstreamBranchRef = mergeRef, Merge = merge, Rebase = rebase, PickHead = pickHead,
            SubmodulesToUpdate = await SubmodulesToUpdateAsync(root, gitDir, status.Files, config, ct).ConfigureAwait(false),
            LfsInstalled = lfsInstalled, HasLfsFiles = lfsFiles,
            DetachedCommits = detachedCommits, StashHead = refs.StashHead, LocalRefs = refs.LocalRefs, RemoteRefs = refs.RemoteRefs, TagRefs = refs.TagRefs,
            UpstreamHead = refs.UpstreamHead,
            Resolutions = decided
        };
        return await comparison.StillExpectedAsync(snapshot, ct).ConfigureAwait(false) ? snapshot : null;
    }

    private async Task<bool> GitLfsRunsAsync(string root, CancellationToken ct)
    {
        if (gitLfs is not { } asked || asked.Item1 != git.Program)
            gitLfs = asked = Tuple.Create(git.Program, (await git.ResultAsync(root, ["lfs", "version"], ct)).ExitCode == 0);
        return asked.Item2;
    }

    private async Task<int> CountDetachedCommitsAsync(string root, string? branch, string? head, CancellationToken ct)
    {
        if (branch is not null || head is null) return 0;
        var result = await git.ResultAsync(root, ["rev-list", "--count", "HEAD", "--not", "--branches", "--remotes", "--tags"], ct);
        if (result.ExitCode != 0) return 0;
        return int.TryParse(GitBytes.StrictLine(result.StandardOutput), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var count) ? count : 0;
    }

    // Git keeps only the onto commit; the branch that points at it names it, a local one before a remote one.
    private async Task<string?> ReadOntoNameAsync(string root, string onto, CancellationToken ct)
    {
        var result = await git.ResultAsync(root, ["for-each-ref", "--points-at=" + onto, "--format=%(refname)", "refs/heads", "refs/remotes"], ct);
        if (result.ExitCode != 0) return null;
        var refs = GitBytes.StrictText(result.StandardOutput).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).ToArray();
        return refs.FirstOrDefault(name => name.StartsWith("refs/heads/", StringComparison.Ordinal)) is { } local ? local["refs/heads/".Length..]
            : refs.FirstOrDefault(name => name.StartsWith("refs/remotes/", StringComparison.Ordinal) && !name.EndsWith("/HEAD", StringComparison.Ordinal)) is { } remote
                ? remote["refs/remotes/".Length..] : null;
    }

    // One read of the refs the snapshot follows: the stash, every local branch, every remote-tracking ref and every tag,
    // the last three as fingerprints that change when a commit, a branch or tag write or a fetch by any program moves one,
    // together with the upstream's commit. Symbolic refs (origin/HEAD) are aliases.
    private async Task<(string? StashHead, string LocalRefs, string RemoteRefs, string TagRefs, string? UpstreamHead)> ReadRefsAsync(string root, string? upstream, CancellationToken ct)
    {
        // A repository with many tags or remote branches lists more than the runner holds, so the list is read as it
        // arrives; a ref name holds no newline.
        return await git.StreamAsync(root, ["for-each-ref", "--format=%(refname)%00%(objectname)%00%(symref)", "refs/heads", "refs/stash", "refs/remotes", "refs/tags"], async (output, cancel) =>
        {
            string? stash = null, upstreamHead = null;
            var locals = new StringBuilder();
            var remotes = new StringBuilder();
            var tags = new StringBuilder();
            using var lines = new StreamReader(output, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            while (await lines.ReadLineAsync(cancel) is { } row)
            {
                if (row.Split('\0') is not [var name, var id, var symref] || id.Length is not (40 or 64)) continue;
                if (name == "refs/stash") stash = id;
                else if (symref.Length != 0) continue;
                else if (name.StartsWith("refs/heads/", StringComparison.Ordinal)) locals.Append(name).Append(' ').Append(id).Append('\n');
                else if (name.StartsWith("refs/tags/", StringComparison.Ordinal)) tags.Append(name).Append(' ').Append(id).Append('\n');
                else
                {
                    remotes.Append(name).Append(' ').Append(id).Append('\n');
                    if (upstream is not null && name == "refs/remotes/" + upstream) upstreamHead = id;
                }
            }
            return (stash, Fingerprint(locals), Fingerprint(remotes), Fingerprint(tags), upstreamHead);
        }, ct);
    }

    // HEAD is the given commit made anew when it has its author, with the date, and its message.
    private async Task<bool> IsMadeAnewAsync(string root, string commit, CancellationToken ct)
    {
        var shown = GitBytes.Text((await git.RunAsync(root, ["show", "-s", "-z", "--no-show-signature", "--format=%an%n%ae%n%aI%n%B", "HEAD", commit, "--"], ct)).StandardOutput).Split('\0');
        return shown.Length > 1 && shown[0] == shown[1];
    }

    // The commit a revert or a cherry-pick in progress applies, as Git wrote it; anything else is no pick Trag can finish.
    private static string? PickHead(string? id) => id is not null && GitObjectId.IsValid(id) ? id : null;

    private static string Fingerprint(StringBuilder refs) =>
        refs.Length == 0 ? "" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refs.ToString())));

    // The submodules Git's own update would check out: those .gitmodules names that are active and update by a checkout.
    // A moved one says so in the status; one never checked out has no repository in its folder, and counts only where the
    // index holds a gitlink, since .gitmodules may still name a submodule the index no longer has.
    private async Task<IReadOnlyList<string>> SubmodulesToUpdateAsync(string root, string gitDir, IReadOnlyList<FileStatus> files, GitConfiguration config, CancellationToken ct)
    {
        var listed = (await SubmodulePaths.ListedAsync(git, root, ct).ConfigureAwait(false))
            .Where(submodule => (config.Value("submodule." + submodule.Name + ".update") ?? submodule.Update ?? "checkout") == "checkout"
                && !Deinitialized(gitDir, submodule, config)).ToArray();
        var active = await ActiveAsync(root, listed, config, ct).ConfigureAwait(false);
        var moved = files.Where(file => file.IsSubmoduleMoved).Select(file => file.Path.Display).Where(active.Contains);
        var gitlinks = await SubmodulePaths.GitlinksAsync(git, root, active.Where(path => !Path.Exists(Path.Combine(root, path, ".git"))), ct).ConfigureAwait(false);
        return [.. moved.Concat(gitlinks).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    // Deinit drops the submodule's configuration and keeps its repository under the worktree's modules folder; a
    // submodule never initialized has neither.
    private static bool Deinitialized(string gitDir, SubmodulePaths.Submodule submodule, GitConfiguration config) =>
        config.Value("submodule." + submodule.Name + ".url") is null && Directory.Exists(Path.Combine(gitDir, "modules", submodule.Name));

    // Git's rule for an active submodule: its own active setting, else whether submodule.active's pathspecs match it, else
    // every submodule is.
    private async Task<HashSet<string>> ActiveAsync(string root, SubmodulePaths.Submodule[] submodules, GitConfiguration config, CancellationToken ct)
    {
        var pathspecs = config.Values("submodule.active");
        var matched = pathspecs.Length == 0 ? null : (await SubmodulePaths.MatchingAsync(git, root, pathspecs, ct).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        return [.. submodules.Where(submodule => config.Value("submodule." + submodule.Name + ".active") is not null
                ? config.Flag("submodule." + submodule.Name + ".active") : matched?.Contains(submodule.Path) ?? true)
            .Select(submodule => submodule.Path)];
    }

    private async Task<GitConfiguration> ReadConfigurationAsync(string root, CancellationToken ct)
    {
        var result = await git.RunAsync(root, ["config", "--null", "--get-regexp", "^(branch\\..*\\.(remote|merge)|remote\\..*\\.(url|pushurl|promisor)|extensions\\.partialclone|core\\.sparsecheckout|core\\.attributesfile|commit\\.gpgsign|tag\\.gpgsign|gpg\\.format|filter\\.lfs\\.process|submodule\\..*\\.(active|update|url)|submodule\\.active)$"], ct, allowMissing: true);
        return new(GitConfigParser.Parse(result.StandardOutput));
    }

    // One remote -v prints every remote's addresses as get-url would, insteadOf and pushInsteadOf applied; a remote it
    // does not list plainly (a line break in its address) is asked by name. An address is only shown, so one that is not
    // UTF-8 is shown as far as it reads.
    private async Task<IReadOnlyList<RepositoryRemote>> ReadRemotesAsync(string root, GitConfiguration config, CancellationToken ct)
    {
        var names = config.RemoteNames().ToArray();
        if (names.Length == 0) return [];
        var listed = GitBytes.Text((await git.RunAsync(root, ["remote", "-v"], ct)).StandardOutput).Split('\n');
        var remotes = new List<RepositoryRemote>();
        foreach (var name in names)
        {
            var fetch = Listed(listed, name, " (fetch)") ?? GitBytes.StrictLine((await git.RunAsync(root, ["remote", "get-url", "--", name], ct)).StandardOutput);
            var push = Listed(listed, name, " (push)") ?? GitBytes.StrictLine((await git.RunAsync(root, ["remote", "get-url", "--push", "--", name], ct)).StandardOutput);
            remotes.Add(new(name, AddressRedactor.Redact(fetch), AddressRedactor.Redact(push)));
        }
        return remotes;
    }

    // The first address remote -v lists for a remote on one side, from its "<name> TAB <address> (fetch|push)" lines.
    private static string? Listed(string[] lines, string name, string side) =>
        lines.Select(line => line.TrimEnd('\r')).FirstOrDefault(line => line.StartsWith(name + "\t", StringComparison.Ordinal)
            && line.EndsWith(side, StringComparison.Ordinal)) is { } found ? found[(name.Length + 1)..^side.Length] : null;
}
