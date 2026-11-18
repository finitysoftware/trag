using Trag.Domain.Naming;

namespace Trag.Domain.Presentation.Text;

// What a clone says: why its address or folder is refused, what it does meanwhile, and how Git's clone ended.
public static class CloneText
{
    public const string AddressInvalid = "The repository address is not valid.";
    public const string FolderMissing = "Enter a local folder for the clone.";
    public const string FolderNotFullPath = "Enter the full path of a folder for the clone.";
    public const string FileInTheWay = "A file already exists at this path. Choose a folder.";
    public const string FolderNotEmpty = "Choose an empty or new folder for the clone.";
    public const string FolderWithoutParent = "Choose a folder inside a drive or your home directory.";

    public const string Cloning = "Cloning repository…";
    public const string NotWorkingCopy = "Git finished, but the cloned folder is not a usable working copy. Review it in your Git tools.";
    public const string Canceled = "Clone canceled.";
    public static readonly string TimedOut = "The server sent nothing for " + Counted.Number(GitLimits.TransferQuietMinutes)
        + " minutes, so Trag stopped the clone. Check the connection or the sign-in, then try again.";
    public const string LfsNotDownloaded = "Git LFS could not download the content of the repository's files, so the clone was removed. Git LFS's details are below.";
    public static string Left(string folder) => "Trag could not remove what the clone wrote to " + folder
        + ", perhaps because another program still holds its files. Delete it before you clone there again.";
    public const string NotFinished = "Clone could not start or finish. Check the folder, the address, your network and the Git installation.";

    // Git's refusal by its likely cause; the details are shown below it either way.
    public const string AuthenticationFailed = "Git authentication failed. Check the account in Settings and its repository permissions, or your existing Git credentials and SSH agent.";
    public const string Unreachable = "The repository address could not be reached. Check your network and the address, then retry.";
    public const string RepositoryMissing = "The host has no repository at this address, or the account in use cannot see it. Check the address and the account, then retry.";
    public const string FolderNotCreated = "Git could not create the folder for the clone. Choose another folder, then retry.";
    public const string TargetExists = "The target folder already exists and is not empty. Choose another folder.";
    public const string Failed = "Git could not clone this repository. Review the details below, then retry.";
}
