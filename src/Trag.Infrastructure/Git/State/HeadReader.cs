using Trag.Domain.Naming;
using Trag.Domain.Status;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.State;

// Where HEAD is, the refs an operation keeps beside it, and what a commit records. Every read runs outside any index transaction: no GIT_INDEX_FILE is set, so
// these calls never see a private index of a write in flight.
internal sealed class HeadReader(GitProcessRunner runner)
{
    private readonly PresetGitRunner writing = new(runner, LocalGitPreset.Writing);

    // One call prints the commit and the branch by its full name, "HEAD" for a detached HEAD; a shortened name would read
    // heads/main while a tag is named main too. Only an unborn branch has no commit to print, and then symbolic-ref names it.
    public async Task<(string? Head, string? Branch)> ReadAsync(string root, CancellationToken ct)
    {
        var result = await writing.ResultAsync(root, ["rev-parse", "HEAD", "--symbolic-full-name", "HEAD"], ct).ConfigureAwait(false);
        if (result.ExitCode == 0)
        {
            if (GitBytes.Text(result.StandardOutput).Split('\n') is not [var head, var name]) throw new FormatException("Invalid HEAD record.");
            return (head.TrimEnd('\r'), RefNames.LocalBranch(name.TrimEnd('\r')));
        }
        var unborn = await writing.ResultAsync(root, ["symbolic-ref", "--quiet", "HEAD"], ct).ConfigureAwait(false);
        if (unborn.ExitCode is not (0 or 1)) throw new GitCommandException(unborn.ExitCode);
        return (null, unborn.ExitCode == 0 ? RefNames.LocalBranch(GitBytes.Text(unborn.StandardOutput)) : null);
    }

    public Task<RootRefs> ReadRootRefsAsync(RepositorySnapshot repository, CancellationToken ct) =>
        RootRefs.ReadAsync(writing, repository.Root, repository.GitDirectory!, repository.CommonDirectory!, ct);

    public async Task<(string Parent, string Tree)> ReadCommitAsync(string root, string id, CancellationToken ct)
    {
        var result = await writing.ResultAsync(root, ["show", "-s", "--no-show-signature", "--format=%P%x00%T", id, "--"], ct).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new GitCommandException(result.ExitCode);
        var fields = GitBytes.Text(result.StandardOutput).Split('\0');
        if (fields.Length != 2) throw new FormatException("Invalid commit metadata.");
        return (fields[0], fields[1]);
    }

    public async Task<bool> NamesParentAsync(string root, string id, CancellationToken ct) =>
        CommitObject.NamesParent((await writing.RunAsync(root, ["cat-file", "commit", id], ct).ConfigureAwait(false)).StandardOutput);
}
