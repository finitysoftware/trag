using Trag.Domain.Accounts;
using Trag.Domain.Publish;

namespace Trag.Domain.Presentation.Text;

// What the publish dialog says: its title and subtitle for a branch or a repository, what Trag is about to do, the target
// branches with the warning for pushing into another one, and why accounts or organizations are missing. And the sync
// panel's card for a push that found the remote's repository missing under the user's own account.
public static class PublishText
{
    public const string Publishing = "Publishing…";
    public const string AddressInvalid = "Enter one remote address without spaces.";
    public const string AccountsNotRead = "Accounts could not be read. Check Settings → Integrations, or use an existing remote URL.";
    public const string OwnersNotListed = "Organizations could not be listed. You can still publish under your own account.";
    public const string NoAccounts = "Add a GitHub or GitLab account in Settings → Integrations to create the repository from Trag, or use an existing remote URL.";

    public static string Title(PublishKind kind) => kind == PublishKind.ExistingRemote ? "Publish branch" : "Publish repository";

    public static string Subtitle(PublishKind kind, PublishContext context) => kind == PublishKind.ExistingRemote
        ? "Push " + context.BranchName + " to a remote and set it as the upstream."
        : "Choose where to publish " + context.RepositoryName + ".";

    // A new remote says what it adds; publishing to an existing remote says where the branch goes.
    public static string Summary(PublishInput input, PublishContext context, string targetSummary) => input.Kind switch
    {
        PublishKind.CreateRepository => "Trag will create the repository, add " + input.RemoteName.Trim() + ", and set " + context.BranchName + " to track it.",
        PublishKind.AddRemote => "Trag will add " + input.RemoteName.Trim() + " with this address and set " + context.BranchName + " to track it.",
        _ => targetSummary
    };

    public static string TargetSummary(string? branch, string? remote, string? target) => "Trag will push " + (branch ?? "the current branch") + " to "
        + (remote ?? "the remote") + "/" + (target ?? branch ?? "the current branch") + " and set it as the upstream.";

    public static string TargetWarning(PublishTarget? target, string? branch) => target is not null && target.PushesIntoOther(branch)
        ? target.Remote + "/" + target.Name + " already exists. Commits of " + branch + " will be pushed into it." : "";

    public static string TargetDisplay(PublishTarget target) => target.IsNew
        ? target.Name + " · new branch on " + target.Remote : target.Name + " · existing on " + target.Remote;

    public static string RepositoryMissing(AccountRepository repository) => repository.Name + " was not found on " + AccountText.ProviderName(repository.Account.Provider);

    public static string CreatesMissing(AccountRepository repository, string branch) => "Trag creates " + repository.Account.UserName + "/" + repository.Name
        + " as a private repository with your " + AccountText.ProviderName(repository.Account.Provider) + " account, then pushes " + branch + ".";
}
