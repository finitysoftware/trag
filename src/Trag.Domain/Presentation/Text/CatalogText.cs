using Trag.Domain.Catalog;

namespace Trag.Domain.Presentation.Text;

// What the repository picker says: the rows of local and hosted repositories, the local list's additions, removals and
// initialization, the account groups, the clone dialog, and why something could not be read or opened.
public static class CatalogText
{
    public const string Welcome = "Select a repository to open it. Add a local repository with New or clone one from an account.";
    public const string ListNotRead = "The repository list could not be read. Check access to the Trag settings folder, then choose Refresh.";
    public const string AccountsNotRead = "Accounts could not be read. Check Settings → Integrations, then choose Refresh.";
    public const string ListAndAccountsNotRead = "The repository list and the accounts could not be read. Check access to the Trag settings folder and Settings → Integrations, then choose Refresh.";
    public const string FolderGone = "This folder is no longer available.";
    public const string AddressUnsafe = "This repository address cannot be opened safely.";

    public const string AddFailed = "The folder could not be added. Check the folder and your Git installation, then try again.";
    public const string InitializeFailed = "Git could not initialize a repository in this folder. Check folder access and your Git installation.";
    public const string RemovedNotSaved = "The entry was removed for this session, but the list could not be saved. Check access to the Trag settings folder.";
    public const string OpenedNotRecorded = "Repository opened, but the repository list could not be updated.";
    public const string FolderMissing = "This folder is no longer available. Choose an existing folder.";
    public const string NotRepository = "This folder is not a Git repository.";
    public const string Untrusted = "Git does not trust this folder's ownership. Review it in your Git tools, then add it again.";
    public const string NeedsNewerGit = "This repository needs a newer Git than the one Trag uses. Choose a newer git.exe in Settings → Git, then add it again.";
    public const string Bare = "This folder is a bare repository, which has no working tree. Add a working copy of it instead.";
    public const string GitDirectory = "This folder holds a repository's Git data, not its files. Add the working copy instead.";
    public static readonly string ListFull = "The repository list holds " + Counted.Number(RepositoryEntry.MaximumListed)
        + " repositories, the most it keeps. Remove one to add another.";
    public static readonly string OpenedNotListed = "Repository opened, but the repository list holds " + Counted.Number(RepositoryEntry.MaximumListed)
        + " repositories, the most it keeps, so it was not added. Remove one to add it.";

    public const string GroupLoading = "Loading repositories…";
    public const string GroupCanceled = "Loading canceled.";
    public const string GroupFailed = "Repositories could not be loaded. Check your connection and the account in Settings, then retry.";
    public const string GroupEmpty = "No repositories were returned for this account.";
    public static readonly string GroupFull = "Trag lists at most " + Counted.Grouped(RemoteRepository.MaximumListed)
        + " repositories, the most recently active ones.";
    // What a group says under its header: that it loads, else what its read said, else that a read found nothing or read
    // every page Trag reads.
    public static string GroupMessage(bool loading, string read, bool loaded, int count, bool reachedLimit) =>
        loading ? GroupLoading : read.Length != 0 ? read : loaded && count == 0 ? GroupEmpty : reachedLimit ? GroupFull : "";
    public static string GroupTitle(string provider, string userName) => provider + " · " + userName;
    public static string GroupHeader(string title, string count) => title + " (" + count + ")";
    public static string LoadRepositories(bool loaded) => loaded ? "Reload repositories" : "Load repositories";

    public const string Cloning = "Cloning…";
    public const string CloneNotDisplayed = "The clone result could not be displayed. Review the target folder in your Git tools.";
    public const string Cloned = "Repository cloned.";
    public const string PickerFailed = "The folder picker could not be opened. Enter the folder path instead.";

    public static string RemovalQuestion(string name) => $"Remove {name} from Trag's list? The folder and its files stay on disk.";
    public static string InitializeQuestion(string path) => $"{path} is not a Git repository yet. Initialize a new repository there?";
    public static string LocalHeader(int count) => "Local repositories (" + Counted.Number(count) + ")";
    public static string Initialized(string name) => $"Initialized an empty repository in {name}. Choose Open to start working in it.";
    public static string Removed(string name) => $"{name} was removed from the list. Its folder and files were not changed.";
    // A count shown alone, as the list and group headers show it.
    public static string Count(int count) => Counted.Number(count);
    // What the picker says about a folder offered to the list: nothing when the offer was canceled, the caller's sentence
    // when it failed, otherwise what became of the folder.
    public static string? Addition(CatalogAdditionOutcome outcome, string failed, string name) => outcome switch
    {
        CatalogAdditionOutcome.Canceled => null,
        CatalogAdditionOutcome.Failed => failed,
        CatalogAdditionOutcome.Missing => FolderMissing,
        CatalogAdditionOutcome.NotRepository => NotRepository,
        CatalogAdditionOutcome.Untrusted => Untrusted,
        CatalogAdditionOutcome.NeedsNewerGit => NeedsNewerGit,
        CatalogAdditionOutcome.Bare => Bare,
        CatalogAdditionOutcome.GitDirectory => GitDirectory,
        CatalogAdditionOutcome.Full => ListFull,
        _ => Added(name, outcome == CatalogAdditionOutcome.AlreadyListed)
    };
    public static string Added(string name, bool alreadyListed) => alreadyListed ? $"{name} is already in the list." : $"{name} added. Choose Open to start working in it.";
    public static string ClonedAs(string name) => $"{name} was cloned. Choose Open to start working in it.";
    public static string CloningFrom(string fullName) => $"Cloning {fullName}…";

    // A local row: its origin, when it was opened, and what the link check found.
    public static string Remote(RepositoryEntry entry) => entry.RemoteUrl ?? "No origin remote";
    // A local row's branch and a hosted row's default branch, when there is one to name.
    public static string Branch(string? name) => name ?? "—";
    public static string LastOpened(RepositoryEntry entry, DateTimeOffset now) => entry.LastOpenedAt is { } opened ? RelativeTime.Opened(opened, now) : "Never";

    public static string LinkStatus(RepositoryLinkStatus status, bool checking) => checking ? "Checking…" : status switch
    {
        RepositoryLinkStatus.Valid => "Valid",
        RepositoryLinkStatus.Missing => "Missing",
        RepositoryLinkStatus.NotRepository => "Not a repository",
        RepositoryLinkStatus.Untrusted => "Not trusted",
        RepositoryLinkStatus.NeedsNewerGit => "Needs newer Git",
        RepositoryLinkStatus.Bare => "Bare repository",
        RepositoryLinkStatus.GitDirectory => "Git data folder",
        _ => "Unchecked"
    };

    public static string Link(RepositoryLinkStatus status, bool checking) => checking ? "Local repository · checking the link…" : status switch
    {
        RepositoryLinkStatus.Valid => "Local repository · Link verified",
        RepositoryLinkStatus.Missing => "Folder not found · move it back or remove this entry",
        RepositoryLinkStatus.NotRepository => "This folder is no longer a Git repository",
        RepositoryLinkStatus.Untrusted => "Git does not trust this folder's ownership · review it in your Git tools",
        RepositoryLinkStatus.NeedsNewerGit => "This repository needs a newer Git · choose one in Settings → Git",
        RepositoryLinkStatus.Bare => "This folder is a bare repository with no working tree · add a working copy instead",
        RepositoryLinkStatus.GitDirectory => "This folder holds a repository's Git data, not its files · add the working copy instead",
        _ => "Local repository · link not checked yet"
    };

    // A hosted row: its clone or when it changed, whether it is cloned, and the page address without the scheme.
    public static string RemoteSubtitle(RemoteRepository repository, string? clonePath, DateTimeOffset now) => clonePath
        ?? (repository.UpdatedAt is { } updated ? "Updated " + RelativeTime.Describe(updated, now) : "Not cloned");
    // The details pane's note: how to get a clone, or where the clone is.
    public static string RemoteNote(string? clonePath) => clonePath ?? "This repository is not cloned on your computer. Choose Clone repository to get a local copy.";
    public static string RemoteState(bool cloned) => cloned ? "Remote repository · Cloned" : "Remote repository · Not cloned";
    public static string Visibility(bool? isPrivate) => isPrivate switch { true => "Private", false => "Public", _ => "Unknown" };
    public static string Updated(RemoteRepository repository, DateTimeOffset now) => repository.UpdatedAt is { } updated ? RelativeTime.Describe(updated, now) : "Unknown";
    public static string ViewOn(string provider) => "View on " + provider;
    public static string WebText(string url) => url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url["https://".Length..] : url;

    // The title of the system dialog a folder is chosen in.
    public const string ChooseFolder = "Choose a folder";
}
