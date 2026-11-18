using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Accounts;
using Trag.Application.Live;
using Trag.Domain.Publish;
using Trag.Domain.Writes;

namespace Trag.Application.Sync;

// The hosted repository the last write found missing, when that write pushed and the remote's address names the
// repository under the user's own Trag account on its host: Trag offers to create it there, private, and to publish the
// branch to it as the push would have. Every write answers the offer again, so one that went through withdraws it, and
// so does one that created the repository: creating it again cannot help the push after it. A snapshot in which the
// remote's address no longer names the repository offered, as after fixing the address in a terminal, withdraws it too.
public sealed partial class MissingRepositoryModel : ObservableObject
{
    private readonly RemotesModel remotes;
    private readonly AccountsModel accounts;

    public MissingRepositoryModel(RemotesModel remotes, AccountsModel accounts)
    {
        this.remotes = remotes;
        this.accounts = accounts;
        var writer = remotes.Repository.Writer;
        writer.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(RepositoryWriter.LastWrite)) Follow(writer.LastWrite); };
        remotes.Repository.SnapshotApplied += (snapshot, _) => { if (Offer is { } offer && !offer.IsNamedBy(snapshot)) Offer = null; };
    }

    [ObservableProperty] public partial MissingRepository? Offer { get; private set; }
    // Ends once the accounts were asked whether one of them can create what the last write found missing.
    public Task Completion { get; private set; } = Task.CompletedTask;
    public bool CanCreate => Offer is not null && remotes.Repository.CanWrite;

    public Task<GitWriteResult?> CreateAsync() => Offer is { } offer && remotes.Repository.Snapshot is { } snapshot
        ? remotes.PublishAsync(new(snapshot, offer.Publish)) : RepositoryWriter.NotRun;

    private void Follow(WriteOutcome? write)
    {
        Offer = null;
        if (write is { Result.MissingRepository: true } && MissingRepository.Retry(write.Request) is { } retry)
            Completion = OfferAsync(write, retry);
    }

    // The accounts may answer after another write has answered the offer again; only the last write's answer counts.
    private async Task OfferAsync(WriteOutcome write, PublishOperation retry)
    {
        var accounts = await this.accounts.TryListAsync(CancellationToken.None) ?? [];
        if (ReferenceEquals(remotes.Repository.Writer.LastWrite, write) && remotes.Repository.Snapshot is { } snapshot)
            Offer = MissingRepository.For(retry, snapshot, accounts);
    }
}
