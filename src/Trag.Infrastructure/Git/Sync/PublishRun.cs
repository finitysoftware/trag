using Trag.Domain.Accounts;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;

namespace Trag.Infrastructure.Git.Sync;

// One publish: the names it works with and the facts about what it has already done. The sentence that prefixes every
// later message is derived from those facts, so an interrupted publish tells the user exactly where it stopped and
// no step has to maintain the wording of the steps before it.
internal sealed class PublishRun(PublishRequest request)
{
    public PublishRequest Request => request;
    public Domain.Publish.PublishOperation Operation => request.Publish;
    public PublishKind Kind => request.Publish.Kind;
    public string Root => request.Expected.Root;
    public string Remote { get; } = request.Publish.RemoteName.Trim();
    public string Branch => request.Publish.Branch;
    // The branch may be published under another name; by default it keeps its own.
    public string Target { get; } = (request.Publish.RemoteBranch ?? request.Publish.Branch).Trim();
    public string Upstream => Remote + "/" + Target;

    // The address the remote is added with: typed by the user, or the clone URL of the repository created for it.
    public string? Address { get; set; } = request.Publish.RemoteUrl?.Trim();

    // The account remembered for the remote: the one that created the repository for it, new remote or existing, or the
    // one chosen for a typed address unless the user chose Git's own credentials. An existing remote whose repository
    // was not created keeps whatever it already has.
    public string? AccountId => Kind == PublishKind.AddRemote ? AccountChoice.TragAccount(Operation.AccountId)
        : Operation.RepositoryName is null ? null : Operation.AccountId;

    // The hosted repository's full name once the provider created it.
    public string? Created { get; set; }
    public bool RemoteAdded { get; set; }
    public bool UpstreamRecorded { get; set; }

    public string Changed
    {
        get
        {
            var remote = Created is null
                ? RemoteAdded ? PublishOutcomeText.RemoteAdded(Remote) : ""
                : RemoteAdded ? PublishOutcomeText.CreatedAndAdded(Created, Remote) : PublishOutcomeText.CreatedOnProvider(Created);
            if (!UpstreamRecorded) return remote;
            var upstream = PublishOutcomeText.NowTracks(Branch, Upstream);
            return remote.Length == 0 ? upstream : remote + " " + upstream;
        }
    }

    public string Prefix => Changed.Length == 0 ? "" : Changed + " ";

    // What a Cancel leaves to do: nothing when nothing changed, the remote to add by hand for a repository created without
    // one, otherwise the push.
    public string Canceled => Changed.Length == 0 ? PublishOutcomeText.Canceled
        : Prefix + (Created is not null && !RemoteAdded ? PublishOutcomeText.CanceledBeforeRemote(AddressRedactor.Redact(Address!), Remote) : PublishOutcomeText.CanceledBeforePush);
}
