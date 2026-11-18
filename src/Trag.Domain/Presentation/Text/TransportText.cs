namespace Trag.Domain.Presentation.Text;

// What a failed transfer says by its likely cause; the details below it are Git's own words.
public static class TransportText
{
    public const string NoSavedCredentials = "Git has no saved credentials for this remote, and Trag never asks for them during a transfer. Choose a Trag account for this remote in the sync panel, or sign in once with Git's own credential helper from a terminal, then retry.";
    public const string Behind = "Push was rejected because the destination has commits you do not have. Fetch and review before pulling or pushing again.";
    public const string ProtectedBranch = "The remote refused because this is its default or protected branch. Change that on the hosting site first.";
    public const string WorkflowScope = "GitHub refused the push because it changes GitHub Actions workflows and this sign-in lacks the workflow permission. Remove and add the GitHub account again in Settings, then push again.";
    public const string OAuthRestricted = "GitHub refused the push because the organization restricts OAuth apps. Approve Trag under the organization's third-party access settings, then push again.";
    public const string RepositoryMissing = "The host has no repository at this remote's address, or the account in use cannot see it. Check the address and the account, then retry.";
    public static string TagsWouldBeClobbered(IReadOnlyList<string> tags) => tags.Count == 1
        ? "The remote's tag " + tags[0] + " differs from the tag of that name here, and this remote's tagOpt setting --tags makes every fetch take all of its tags, so nothing was fetched. Delete the tag here to take the remote's, or remove the tagOpt setting."
        : "The remote's tags " + string.Join(", ", tags) + " differ from the tags of those names here, and this remote's tagOpt setting --tags makes every fetch take all of its tags, so nothing was fetched. Delete those tags here to take the remote's, or remove the tagOpt setting.";
    public const string AuthenticationFailed = "Git authentication failed. Check the selected account in Settings, its repository permissions, or your existing SSH agent and Git credentials, then retry.";
    public static string SubmodulesNotPushed(IReadOnlyList<string> paths) => paths.Count == 1
        ? "Push submodule " + paths[0] + " first. This branch records a commit of it that none of its remotes has, so others could not fetch that commit."
        : "Push submodules " + string.Join(", ", paths) + " first. This branch records commits of them that none of their remotes has, so others could not fetch those commits.";
    public const string Failed = "Git rejected the transfer or the connection failed. Check the remote, network, account permissions and organization approval; details are shown below.";
}
