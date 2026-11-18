namespace Trag.Domain.Presentation.Text;

// What a hosting provider's answer means when it is not the one Trag asked for: unreachable, refused, limited or
// unreadable, and why it would not list owners or create a repository.
public static class ProviderText
{
    public const string Unreachable = "Cannot reach the provider securely. Check your connection, server address and certificate trust.";
    public const string TimedOut = "The provider timed out. Check your connection and try again.";
    public const string ResponseTooLarge = "The provider response is too large.";
    public const string TokenRevoked = "The token has expired or was revoked. Sign in again or replace your GitLab token.";
    public const string AccessDenied = "Access was denied. Check token permissions and organization approval; GitLab identity requires read_user.";
    public const string RateLimited = "The provider is limiting requests. Wait a moment and try again.";
    public const string Failed = "The provider could not complete the request. Check its HTTPS address and service availability.";
    public const string IdentityUnreadable = "The provider returned an unreadable identity response.";
    public const string IdentityInvalid = "The provider did not return a valid account identity.";
    public const string OwnersUnreadable = "The provider returned an unreadable organization list.";
    public const string RepositoriesUnreadable = "The provider returned an unreadable repository list.";
    public const string NoHttpsCloneAddress = "The provider listed repositories, but none has an HTTPS clone address Trag can use.";

    // Owners and creating a repository; site is GitHub or GitLab.
    public const string OrganizationsNotListed = "Organizations could not be listed with this sign-in. Remove and add the GitHub account again in Settings to grant organization access, or publish under your own account.";
    public const string GroupsNotListed = "Groups could not be listed: the token needs the api scope. You can still publish under your own account.";
    public const string OwnerMissing = "Choose an owner from the list.";
    public const string GroupMissing = "Choose a group from the list.";
    public const string OwnerNotFound = "The owner was not found or is not accessible with this account. Choose another owner.";
    public const string GitHubCreateDenied = "GitHub denied creating the repository. Check your permissions for this owner and the organization's OAuth app approval.";
    public const string GitLabCreateDenied = "GitLab denied creating the project. The token needs the api scope; replace it in Settings.";
    public static string CreateUnreadable(string site) => site + " returned an unreadable response. Check whether the repository exists on the website before retrying.";
    public static string AddressUnreadable(string site) => site + " created the repository but returned an unreadable address. Find it on the website and use its URL as an existing remote.";
    public static string NotAccepted(string site) => site + " did not accept the repository. A repository with this name may already exist for this owner, or the name is not allowed there.";
}
