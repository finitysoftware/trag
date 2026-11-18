using System.Text.RegularExpressions;

namespace Trag.Infrastructure.Git.Write;

// A branch's settings, its upstream among them, are the section branch.<name> of the repository's configuration, which
// Git renames or removes with the branch only after its refs, through a lock of the whole file. A Git stopped before
// that leaves the section under a name no branch has; the write finishes it as Git would have. False when Git refuses.
internal static class BranchSection
{
    public static async Task<bool> FollowAsync(WriteCommandRunner git, string root, string name, string? newName, CancellationToken ct)
    {
        var section = "branch." + name;
        var left = await git.RunAsync(root, ["config", "--local", "--name-only", "--get-regexp", "^" + Regex.Escape(section) + "\\."], ct).ConfigureAwait(false);
        if (left.ExitCode != 0) return true;
        string[] follow = newName is null ? ["config", "--remove-section", section] : ["config", "--rename-section", section, "branch." + newName];
        return (await git.RunAsync(root, follow, ct).ConfigureAwait(false)).ExitCode == 0;
    }
}
