using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Branches;
using Trag.Application.Live;
using Trag.Application.Publish;
using Trag.Application.Sync;
using Trag.Domain.Accounts;
using Trag.Domain.Branches;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;

namespace Trag.ViewModels.Publish;

// Coordinates the dialog. Context owns repository facts, Draft owns input and the outcome notice, Accounts owns account reads.
public sealed partial class PublishViewModel : ObservableObject
{
    private readonly RemotesModel remotes;
    private readonly Repository repository;
    private readonly BranchesModel branches;
    public PublishAccountsViewModel Accounts { get; }
    public PublishDraft Draft { get; } = new(PublishContext.Empty, BranchCatalog.Empty);
    public PublishViewModel(PublishAccountsModel accounts, RemotesModel remotes, BranchesModel branches)
    {
        this.remotes = remotes;
        repository = remotes.Repository;
        this.branches = branches;
        repository.Requery.Track(OpenPublishCommand, PublishCommand);
        remotes.Publishing.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Operation.IsBusy)) return;
            OnPropertyChanged(nameof(IsPublishing));
            OnPropertyChanged(nameof(CanEditPublish));
            foreach (var command in new IRelayCommand[] { PublishCommand, ChoosePublishGitHubCommand, ChoosePublishGitLabCommand, UsePublishProviderCommand,
                UsePublishUrlCommand, SetPublishPrivateCommand, SetPublishPublicCommand }) command.NotifyCanExecuteChanged();
        };
        Accounts = new(accounts);
        Accounts.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PublishAccountsViewModel.CanCreateRepository)) PublishCommand.NotifyCanExecuteChanged(); };
        repository.SnapshotApplied += (_, _) => ApplyContext(remotes.PublishContext);
        branches.List.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(branches.List.Value)) RefreshOpenTargets(); };
        Draft.PropertyChanged += DraftChanged;
    }

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(OpenPublishCommand))]
    public partial PublishContext Context { get; private set; } = PublishContext.Empty;

    // The repository's remotes run the confirmed publish.
    public bool IsPublishing => remotes.Publishing.IsBusy;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPublishDetails))]
    public partial string PublishDetails { get; private set; } = "";

    public bool HasPublishDetails => PublishDetails.Length != 0;
    public bool CanEditPublish => !IsPublishing;

    private void DraftChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PublishDraft.RemoteUrl))
            Accounts.RefreshUrlAccounts(Draft.RemoteUrl);
        if (e.PropertyName is nameof(PublishDraft.IsValid) or nameof(PublishDraft.IsOpen))
            PublishCommand.NotifyCanExecuteChanged();
    }

    private void ApplyContext(PublishContext context)
    {
        Context = context;
        Draft.UpdateContext(context);
    }

    private bool CanOpenPublish() => Context.CanOpen && !repository.IsBusy;

    [RelayCommand(CanExecute = nameof(CanOpenPublish), AllowConcurrentExecutions = true)]
    private async Task OpenPublishAsync()
    {
        if (!CanOpenPublish())
            return;
        CancelLoads();
        PublishDetails = "";
        Draft.Open(Context, branches.List.Value);
        Accounts.RefreshUrlAccounts(Draft.RemoteUrl);
        // The targets are the branch list's, which follows the repository; a read still in flight arrives through it.
        await Task.WhenAll(Accounts.LoadAsync(), branches.List.Completion);
    }

    private void RefreshOpenTargets()
    {
        if (Draft.IsOpen && Draft.IsBranchMode)
            Draft.UpdateTargets(branches.List.Value);
    }

    [RelayCommand]
    private void CancelPublish()
    {
        if (IsPublishing)
        {
            remotes.CancelPublish();
            return;
        }
        CloseDialog();
    }

    private void CloseDialog()
    {
        Draft.Close();
        CancelLoads();
        PublishDetails = "";
    }

    [RelayCommand(CanExecute = nameof(CanEditPublish))]
    private void UsePublishProvider()
    {
        if (CanEditPublish) Draft.Mode = PublishKind.CreateRepository;
    }

    [RelayCommand(CanExecute = nameof(CanEditPublish))]
    private void UsePublishUrl()
    {
        if (CanEditPublish) Draft.Mode = PublishKind.AddRemote;
    }

    [RelayCommand(CanExecute = nameof(CanEditPublish))]
    private void SetPublishPrivate()
    {
        if (CanEditPublish) Draft.IsPrivate = true;
    }

    [RelayCommand(CanExecute = nameof(CanEditPublish))]
    private void SetPublishPublic()
    {
        if (CanEditPublish) Draft.IsPrivate = false;
    }

    [RelayCommand(CanExecute = nameof(CanEditPublish))]
    private void ChoosePublishGitHub() => ChooseProvider(AccountProvider.GitHub);

    [RelayCommand(CanExecute = nameof(CanEditPublish))]
    private void ChoosePublishGitLab() => ChooseProvider(AccountProvider.GitLab);

    private void ChooseProvider(AccountProvider provider)
    {
        if (CanEditPublish) Accounts.ChooseProvider(provider);
    }

    private bool CanPublish() => Draft.IsOpen && !IsPublishing && repository.CanWrite && Draft.IsValid
        && (!Draft.IsProviderMode || Accounts.CanCreateRepository);

    // The repository's remotes take what the user confirmed before the dialog shows that it publishes; the status line
    // follows the publish, and so does the dialog's notice while it publishes. A publish that is done closes the dialog;
    // any other end stays in it.
    [RelayCommand(CanExecute = nameof(CanPublish))]
    private async Task PublishAsync()
    {
        if (!CanPublish() || remotes.ConfirmPublish(Draft.Input, Accounts.AccountFor(Draft.Mode), Accounts.OwnerId) is not { } publish) return;
        Draft.Notice = PublishText.Publishing;
        PublishDetails = "";
        if (await publish.RunAsync(value => Draft.Notice = value) is not { } refused) CloseDialog();
        else (Draft.Notice, PublishDetails) = (refused.Message, refused.Details);
    }

    private void CancelLoads() => Accounts.CancelLoads();
}
