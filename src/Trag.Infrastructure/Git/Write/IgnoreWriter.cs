using System.Text;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Write;

// An untracked file ignored by a line appended to the repository's root .gitignore, made when there is none, as
// IgnoreFile adds it. Git's check-ignore then says whether the file is ignored now; a line added at the end can only be
// overridden by a .gitignore in one of the file's folders.
internal sealed class IgnoreWriter(WriteCommandRunner git)
{
    public async Task<GitWriteResult> IgnoreAsync(IgnoreRequest request, RepositorySnapshot fresh, CancellationToken ct)
    {
        var file = Path.Combine(fresh.Root, ".gitignore");
        // Git does not read a .gitignore that is a link, and a line appended through one would land outside the repository.
        if (RepositoryPathGuard.HasLink(fresh.Root, file)) return GitWriteResult.Rejected(IgnoreText.GitignoreIsLink);
        var bytes = File.Exists(file) ? await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false) : [];
        var added = IgnoreFile.Addition(bytes, request.Pattern);
        if (added is not null) await File.AppendAllTextAsync(file, added, new UTF8Encoding(false), ct).ConfigureAwait(false);
        // check-ignore refuses the literal pathspec magic writes otherwise run with, yet reads its paths as pathspecs: a
        // leading ':' as magic, and against the index as a glob, where "[a].txt" is a tracked a.txt and so never ignored.
        // "./" keeps the name from being magic, and --no-index skips the index; the file is untracked.
        var check = await git.RunAsync(fresh.Root, ["check-ignore", "--quiet", "--no-index", "--", "./" + request.Path.Display], ct, literalPathspecs: false).ConfigureAwait(false);
        return check.ExitCode == 0
            ? new(GitWriteOutcome.Succeeded, IgnoreText.Ignored(request.Pattern), GitOutputText.Details(check))
            : GitWriteResult.Rejected(added is null ? IgnoreText.NotIgnored : IgnoreText.AddedButListed(request.Pattern), GitOutputText.Details(check));
    }
}
