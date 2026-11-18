using System.Text.RegularExpressions;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Sync;

// What a refused or failed transfer means to the user, read from Git's own output and the hosting provider's words.
internal static partial class TransportFailureText
{
    // OpenSSH names the methods the server refused after its "Permission denied"; the operating system's, for a file Git
    // may not create here, ends the line.
    private static readonly string[] CredentialPhrases = ["authentication", "Permission denied ("];

    // Trag forbids every credential prompt during a transfer, so a remote Git has nothing saved for fails on these
    // words before the network is reached. The remedy is a choice of account, not a check of the connection.
    private static readonly string[] MissingCredentialPhrases = ["could not read Username", "could not read Password", "terminal prompts disabled", "Cannot prompt because"];

    // Git's own line for credentials the remote refused answers before the missing ones: a refused account is no missing
    // one, though Git's next ask for a password fails as well.
    private static readonly (string[] Phrases, string Message)[] Table =
    [
        (["non-fast-forward", "fetch first"],
            TransportText.Behind),
        (["refusing to delete the current branch", "protected branch"],
            TransportText.ProtectedBranch),
        (["without `workflow` scope"],
            TransportText.WorkflowScope),
        (["OAuth App access restrictions"],
            TransportText.OAuthRestricted),
        (["Authentication failed"],
            TransportText.AuthenticationFailed),
        (MissingCredentialPhrases,
            TransportText.NoSavedCredentials),
        (CredentialPhrases,
            TransportText.AuthenticationFailed)
    ];

    // Whether the transfer failed on its credentials, refused by the remote or never available to Git, as opposed to
    // the network or anything else. Either way, trying again unchanged would only fail again.
    public static bool RefusesCredentials(GitResult result)
    {
        var details = GitOutputText.Said(result);
        return Array.Exists([.. CredentialPhrases, .. MissingCredentialPhrases], phrase => details.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }

    // Git's words for an address its HTTPS host answers with 404, and Git LFS's for the same answer to the files it sends
    // before the commits: GitHub and GitLab answer so for a repository that does not exist and for one the credentials
    // may not see.
    public static bool MissesRepository(string details) => MissingRepository().IsMatch(details);

    public static string Explain(GitResult result)
    {
        var details = GitOutputText.Said(result);
        var tags = ClobberedTag().Matches(details).Select(match => match.Groups["tag"].Value).ToArray();
        return MissesRepository(details) ? TransportText.RepositoryMissing
            : tags.Length > 0 ? TransportText.TagsWouldBeClobbered(tags)
            : GitFailureText.Classify(details, Table, TransportText.Failed);
    }

    [GeneratedRegex("repository '[^']*' not found|Repository or object not found", RegexOptions.CultureInvariant)]
    private static partial Regex MissingRepository();

    // Git's line for each tag here that a fetch of all the remote's tags would overwrite.
    [GeneratedRegex(@"\[rejected\]\s+(?<tag>\S+)\s+->\s+\S+\s+\(would clobber existing tag\)", RegexOptions.CultureInvariant)]
    private static partial Regex ClobberedTag();
}
