using Trag.Domain.Naming;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.State;

// Whether Git itself accepts a ref is decided by check-ref-format, after Domain has answered the question about its
// characters (RefNames). What a rejected name means to the user stays with the caller, because each one says it its own way.
internal sealed class GitRefNameValidator(GitProcessRunner runner)
{
    // The two questions a branch name has to answer, in the order they are asked. check-ref-format carries no preset. It
    // prints the branch a name stands for, and nothing for a name it refuses, so Git's shorthand for another branch,
    // @{-1} or @{u}, comes back as that branch's name; @ comes back as itself, though Git reads it as HEAD wherever it
    // takes a revision.
    public async Task<bool> IsValidBranchNameAsync(string root, string name, CancellationToken ct) =>
        RefNames.HasSafeCharacters(name, GitLimits.NameLength) && name != "@"
        && GitBytes.Text((await runner.RunAsync(root, ["check-ref-format", "--branch", name], ct).ConfigureAwait(false)).StandardOutput) == name;

    // A remote's name: Domain's rule, then Git's for the refs it would hold.
    public async Task<bool> IsValidRemoteNameAsync(string root, string remote, CancellationToken ct) =>
        RemoteNames.IsValid(remote) && await IsValidRefAsync(root, "refs/remotes/" + remote + "/check", false, ct).ConfigureAwait(false);

    public async Task<bool> IsValidRefAsync(string root, string name, bool pattern, CancellationToken ct) =>
        name.Length <= GitLimits.RefLength && !name.Any(char.IsControl) && (await runner.RunAsync(root,
            ["check-ref-format", .. pattern ? new[] { "--refspec-pattern" } : [], name], ct).ConfigureAwait(false)).ExitCode == 0;
}
