using Trag.Domain.Accounts;

namespace Trag.Domain.Presentation.Text;

// What the account settings say: the provider's name and icon, where credentials are kept, each step of signing in,
// checking and removing an account, and how an account operation ended.
public static class AccountText
{
    public const string AddAccount = "Add an account to authenticate HTTPS Git operations.";
    public const string CheckingStore = "Checking your OS credential store…";
    public const string Loaded = "GitHub and GitLab accounts are separate from your Git commit author.";
    public const string GitLabTokenHelp = "Create a GitLab personal access token with read_user and write_repository permissions; add api if Trag should create projects for you.";
    public const string VerifyingGitLab = "Verifying your GitLab account…";
    public const string CheckingIdentity = "Checking account identity…";
    public const string Removed = "Account removed from this computer. Review any repositories that used it before syncing.";
    public const string OpenDevicePage = "Open " + AccountMapping.GitHubDevicePage + " in your browser and enter the displayed code.";
    // The first account choice, which leaves the credentials to Git, and how every list names an account.
    public const string GitCredentials = "Existing Git credentials / SSH agent";
    public static string Display(AccountInfo account) => $"{account.UserName} · {account.TransportOrigin.GetLeftPart(UriPartial.Authority)}";
    // Which account a remote is used with, and why.
    public const string RemoteUsesGit = "Using your existing Git credentials. SSH uses its configured agent.";
    public const string RemoteUsesSshKey = "SSH uses its configured key independently of the hosting account.";
    public const string RemoteUsesSaved = "Using the saved account for this repository.";
    public static string RemoteUsesOnlyMatch(AccountInfo account) => "Using " + account.Display + ", the only account matching this remote. Pick another choice below to change it.";
    public const string Canceled = "Sign-in or account operation canceled. You can try again.";
    public const string Failed = "The account operation could not finish. Check your connection and credential store, then retry.";

    public static string ProviderName(AccountProvider provider) => provider == AccountProvider.GitHub ? "GitHub" : "GitLab";

    public static string Connected(AccountInfo account) => $"Connected as {account.UserName}. Repository permissions are checked when you sync.";
    public static string Verified(AccountInfo account) => $"API identity verified for {account.UserName}. Git access depends on repository and organization permissions.";

    public static string Vault(SecretStoreStatus status) => status switch
    {
        SecretStoreStatus.Available => "Credentials are saved locally in your OS credential store.",
        SecretStoreStatus.Locked => "Credential store locked · unlock it in your system keyring, then retry.",
        _ => "Credential store unavailable · retry after unlocking, or explicitly choose a session-only account."
    };

    public static string DeviceLogin(DeviceLoginState state) => state switch
    {
        DeviceLoginState.RequestingCode => "Requesting a GitHub sign-in code…",
        DeviceLoginState.Waiting => "Enter this code in your browser and approve access. Waiting for GitHub…",
        DeviceLoginState.SlowedDown => "GitHub requested a longer wait. Keep the browser open to complete sign-in.",
        _ => "Verifying the GitHub account identity…"
    };

    // The label an OS keyring shows for a credential Trag saved.
    public const string SecretLabel = "Trag account";
}
