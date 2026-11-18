using System.Text;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Diagnostics;

// The one way Git's own output reaches a user: standard error first, because that is where Git explains itself, and
// nothing leaves here unredacted. A caller that has its own secret to hide redacts the raw text first and this last.
// A command that never returned a result has no details, so a verification hands over whatever it has, or null.
internal static class GitOutputText
{
    public static string Raw(GitResult result) => Encoding.UTF8.GetString(result.StandardError) + "\n" + Encoding.UTF8.GetString(result.StandardOutput);

    // What Git said, read for what it means, with the secret the result hides only from the details still in it.
    public static string Said(GitResult result) => GitOutputRedaction.Redact(Raw(result));

    public static string Details(GitResult? result) => result is null ? ""
        : GitOutputRedaction.Redact(result.HideInDetails is { } hide ? hide(Raw(result)) : Raw(result));
}
