using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Sync;

namespace Trag.Infrastructure.Git.Catalog;

// Git's own clone output, turned into the one sentence that tells the user what to do next. The details are shown
// below it either way, so the sentence only has to name the likely cause.
internal static class CloneFailureText
{
    private static readonly (string[] Phrases, string Message)[] Table =
    [
        // A folder Git may not create ends in "Permission denied" too, the words an SSH host refuses a key with.
        (["could not create work tree dir"],
            CloneText.FolderNotCreated),
        // Git says a host refused the address with 403 in curl's words, inside the line that it could not access it.
        (["Authentication failed", "Permission denied", "could not read Username", "HTTP 403", "returned error: 403"],
            CloneText.AuthenticationFailed),
        (["Could not resolve host", "unable to access"],
            CloneText.Unreachable),
        (["already exists"], CloneText.TargetExists)
    ];

    // A host's 404 reads as it does for a fetch or a push.
    public static string Explain(string details) =>
        TransportFailureText.MissesRepository(details) ? CloneText.RepositoryMissing : GitFailureText.Classify(details, Table, CloneText.Failed);
}
