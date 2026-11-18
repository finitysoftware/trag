namespace Trag.Domain.Presentation.Text;

// Why an account operation was refused or failed: the account, its token, the OS credential store, the settings file,
// and the account a repository's remote uses.
public static class AccountFailureText
{
    public const string AccountRemoved = "This account was removed. Choose another account in Settings.";
    public const string GitLabAddressInvalid = "Enter the GitLab HTTPS address, including its port and installation path if needed.";
    public static string TooMany(int limit) => "Trag supports up to " + Counted.Number(limit) + " accounts. Remove an unused account before adding another.";
    public const string IdentityChanged = "The provider returned a different identity. Remove this account and sign in again.";
    public const string DestinationMismatch = "This account does not match the HTTPS destination. Review the account selection.";
    public const string TokenInvalid = "Enter a valid access token without spaces or line breaks.";
    public const string RenewedForOther = "The renewed token belongs to another identity. Sign in again.";

    // The OS credential store, and the settings file the accounts are listed in.
    public const string SavedStoreLocked = "This account is saved in the OS credential store. Unlock the store to reconnect, or remove the saved account first.";
    public const string StoreNeeded = "Unlock your OS credential store and retry, or select Remember only until Trag closes.";
    public const string RemoveNeedsStore = "Unlock your OS credential store and retry removal. The saved account has been kept so its credential can still be deleted.";
    public const string StoreLocked = "Your OS credential store is locked or unavailable. Unlock it and retry.";
    public const string CredentialMissing = "The saved credential is missing. Sign in to this account again.";
    public const string CredentialUnreadable = "This credential cannot be read. Sign in again.";
    public const string CredentialNotSaved = "The credential could not be saved. Unlock your OS credential store and sign in again, or explicitly choose a session-only account.";
    public const string SettingsNotRead = "Account settings could not be read. Check access to the Trag settings folder before retrying.";
    public const string SettingsNotSaved = "Account settings could not be saved. Check access to the Trag settings folder and retry.";

    // The account a repository's remote uses.
    public const string SelectionNotRead = "The repository account selection could not be read.";
    public const string SelectionNotSaved = "The account selection could not be saved. Check the repository configuration lock and retry.";
    public const string NoMatchingAccount = "Choose an account matching both destinations, or use your existing Git credentials.";
}
