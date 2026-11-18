using Trag.Domain.Accounts;
using Trag.Domain.Catalog;
using Trag.Domain.Presentation.Text;
using Trag.TestSupport;
using Trag.Domain.Presentation;

namespace Trag.Domain.UnitTests.Presentation.Text;

public sealed class CatalogTextTests
{
    // A canceled offer says nothing; a failed one says what the caller's failure means.
    [Fact]
    public void WhenAFolderIsOfferedToTheList_ShouldSayWhatBecameOfIt()
    {
        Assert.Null(CatalogText.Addition(CatalogAdditionOutcome.Canceled, "It failed.", ""));
        Assert.Equal("It failed.", CatalogText.Addition(CatalogAdditionOutcome.Failed, "It failed.", ""));
        Assert.Equal(CatalogText.FolderMissing, CatalogText.Addition(CatalogAdditionOutcome.Missing, "It failed.", ""));
        Assert.Equal(CatalogText.NotRepository, CatalogText.Addition(CatalogAdditionOutcome.NotRepository, "It failed.", ""));
        Assert.Equal(CatalogText.ListFull, CatalogText.Addition(CatalogAdditionOutcome.Full, "It failed.", ""));
        Assert.Equal("beta added. Choose Open to start working in it.", CatalogText.Addition(CatalogAdditionOutcome.Added, "It failed.", "beta"));
        Assert.Equal("beta is already in the list.", CatalogText.Addition(CatalogAdditionOutcome.AlreadyListed, "It failed.", "beta"));
    }

    [Fact]
    public void WhenALocalRowsLinkIsBeingChecked_ShouldSayChecking() =>
        Assert.Equal("Checking…", CatalogText.LinkStatus(RepositoryLinkStatus.Valid, checking: true));

    [Fact]
    public void WhenALocalRowsLinkIsValid_ShouldSayItIsVerified() =>
        Assert.Equal("Local repository · Link verified", CatalogText.Link(RepositoryLinkStatus.Valid, false));

    [Fact]
    public void WhenALocalRowsFolderIsMissing_ShouldSayToMoveItBackOrRemoveTheEntry() =>
        Assert.Equal("Folder not found · move it back or remove this entry", CatalogText.Link(RepositoryLinkStatus.Missing, false));

    [Fact]
    public void WhenALocalRowsFolderIsNotARepository_ShouldSaySo() =>
        Assert.Equal("Not a repository", CatalogText.LinkStatus(RepositoryLinkStatus.NotRepository, false));

    [Fact]
    public void WhenALocalRepositoryHasNoOrigin_ShouldSaySo() =>
        Assert.Equal("No origin remote", CatalogText.Remote(new("C:/repo", "repo", DateTimeOffset.UnixEpoch)));

    [Fact]
    public void WhenTheLocalListIsShown_ShouldCountItsRepositoriesInTheHeader() => Assert.Equal("Local repositories (3)", CatalogText.LocalHeader(3));

    [Fact]
    public void WhenAnAddedRepositoryIsAlreadyListed_ShouldSaySo() =>
        Assert.Equal("repo is already in the list.", CatalogText.Added("repo", alreadyListed: true));

    [Fact]
    public void WhenAHostedRepositoryIsListed_ShouldBeNamedWithoutItsOwner()
    {
        var repository = Sandbox();
        Assert.Equal("sandbox", repository.Name);
    }

    [Fact]
    public void WhenTheWebAddressIsHttpsOnTheAccountsHost_ShouldOpenIt()
    {
        var (account, repository) = (FakeAccountService.GitHub(), Sandbox());
        Assert.Equal(new Uri("https://github.com/contoso/sandbox"), repository.SafeWebAddress(account));
    }

    [Fact]
    public void WhenTheWebAddressIsNotHttps_ShouldNotOpenIt()
    {
        var (account, repository) = (FakeAccountService.GitHub(), Sandbox());
        Assert.Null((repository with { WebUrl = "http://github.com/contoso/sandbox" }).SafeWebAddress(account));
    }

    [Fact]
    public void WhenTheWebAddressCarriesAUserName_ShouldNotOpenIt()
    {
        var (account, repository) = (FakeAccountService.GitHub(), Sandbox());
        Assert.Null((repository with { WebUrl = "https://user@github.com/contoso/sandbox" }).SafeWebAddress(account));
    }

    [Fact]
    public void WhenTheWebAddressIsOnAnotherHost_ShouldNotOpenIt()
    {
        var (account, repository) = (FakeAccountService.GitHub(), Sandbox());
        Assert.Null((repository with { WebUrl = "https://evil.example/contoso/sandbox" }).SafeWebAddress(account));
    }

    [Fact]
    public void WhenTheWebAddressIsShown_ShouldLeaveOutTheScheme()
    {
        var repository = Sandbox();
        Assert.Equal("github.com/contoso/sandbox", CatalogText.WebText(repository.WebUrl));
    }

    [Fact]
    public void WhenAHostedRepositoryIsNotClonedAndHasNoUpdateTime_ShouldSayNotCloned()
    {
        var repository = Sandbox();
        Assert.Equal("Not cloned", CatalogText.RemoteSubtitle(repository, null, DateTimeOffset.UnixEpoch));
    }

    // The details pane says how to get a clone, or where the clone is.
    [Fact]
    public void WhenAHostedRepositoryIsShownInItsDetails_ShouldSayHowToCloneItOrWhereItsCloneIs()
    {
        Assert.Equal("This repository is not cloned on your computer. Choose Clone repository to get a local copy.", CatalogText.RemoteNote(null));
        Assert.Equal("C:/clone", CatalogText.RemoteNote("C:/clone"));
    }

    [Fact]
    public void WhenAHostedRepositoryIsCloned_ShouldShowWhereItsCloneIs()
    {
        var repository = Sandbox();
        Assert.Equal("C:/clone", CatalogText.RemoteSubtitle(repository, "C:/clone", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void WhenAHostedRepositoryIsPrivate_ShouldSaySo() => Assert.Equal("Private", CatalogText.Visibility(true));

    [Fact]
    public void WhenTheProviderIsGitHub_ShouldBeNamedGitHub() => Assert.Equal("GitHub", AccountText.ProviderName(AccountProvider.GitHub));

    [Fact]
    public void WhenTheProviderIsGitLab_ShouldShowTheGitLabIcon() => Assert.Equal("gitlab", IconKinds.Account(AccountProvider.GitLab));

    [Fact]
    public void WhenTheCredentialStoreIsLocked_ShouldSaySo() => Assert.StartsWith("Credential store locked", AccountText.Vault(SecretStoreStatus.Locked));

    [Fact]
    public void WhenSignInWaitsForTheBrowser_ShouldAskToEnterTheCodeThere() =>
        Assert.StartsWith("Enter this code in your browser", AccountText.DeviceLogin(DeviceLoginState.Waiting));

    [Fact]
    public void WhenAnAccountIsConnected_ShouldNameTheUserAndSayPermissionsAreCheckedOnSync() =>
        Assert.Equal("Connected as alice. Repository permissions are checked when you sync.", AccountText.Connected(FakeAccountService.GitHub()));

    [Fact]
    public void WhenAGroupLoads_ShouldSaySoOverWhatAnEarlierReadSaid() =>
        Assert.Equal(CatalogText.GroupLoading, CatalogText.GroupMessage(loading: true, read: CatalogText.GroupFailed, loaded: false, count: 0, reachedLimit: false));

    [Fact]
    public void WhenAGroupsReadSaysWhyItFailed_ShouldSayThat() =>
        Assert.Equal(CatalogText.GroupFailed, CatalogText.GroupMessage(loading: false, read: CatalogText.GroupFailed, loaded: true, count: 0, reachedLimit: false));

    [Fact]
    public void WhenAGroupsReadFoundNothing_ShouldSayItIsEmpty() =>
        Assert.Equal(CatalogText.GroupEmpty, CatalogText.GroupMessage(loading: false, read: "", loaded: true, count: 0, reachedLimit: false));

    [Fact]
    public void WhenAGroupShowsRepositories_ShouldSayNothing() =>
        Assert.Equal("", CatalogText.GroupMessage(loading: false, read: "", loaded: true, count: 2, reachedLimit: false));

    // Trag reads ten pages of a hundred, most recently active first, so an account whose tenth page was full may hold older
    // repositories; the pages decide, not the rows left after some were left out.
    [Fact]
    public void WhenTragReadAllThePagesItReads_ShouldSayItListsOnlyTheMostRecentlyActive()
    {
        Assert.Equal("Trag lists at most 1,000 repositories, the most recently active ones.", CatalogText.GroupMessage(loading: false, read: "", loaded: true, count: 990, reachedLimit: true));
        Assert.Equal("", CatalogText.GroupMessage(loading: false, read: "", loaded: true, count: 1000, reachedLimit: false));
    }

    [Fact]
    public void WhenAGroupWasNeverLoaded_ShouldSayNothing() =>
        Assert.Equal("", CatalogText.GroupMessage(loading: false, read: "", loaded: false, count: 0, reachedLimit: false));

    // A private repository on GitHub that names no time it was last updated.
    private static RemoteRepository Sandbox() =>
        new("contoso/sandbox", "https://github.com/contoso/sandbox.git", "https://github.com/contoso/sandbox", "main", true, null);
}
