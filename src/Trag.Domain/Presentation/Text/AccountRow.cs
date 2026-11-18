using Trag.Domain.Accounts;

namespace Trag.Domain.Presentation.Text;

// One connected account as the Settings card shows it: the two sentences under the name, the account itself for the
// commands, and whether its row asks to confirm its removal. AccountInfo stays a record of what is true about the account.
public sealed record AccountRow(AccountInfo Account, bool ConfirmsRemoval = false)
{
    public string Display => Account.Display;

    public string StorageText => Account.IsPersistent
        ? "Saved in your OS credential store" : "This session only · sign in again after closing Trag";

    public string IdentityText => "Identity checked "
        + RelativeTime.LocalStamp(Account.VerifiedAt)
        + " · repository access is checked when you sync";
}
