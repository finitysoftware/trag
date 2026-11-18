using Trag.Application.Sync;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;

namespace Trag.Application.Publish;

// A publish the dialog confirmed: the repository's remotes built its write over the snapshot shown when the user
// confirmed, so what runs later is what the user saw. The dialog runs it once it shows that it publishes, as the remotes'
// publishing operation, and learns whether the end leaves it anything to say.
public sealed class ConfirmedPublish
{
    private readonly RemotesModel remotes;
    private readonly PublishRequest request;

    internal ConfirmedPublish(RemotesModel remotes, PublishRequest request)
    {
        this.remotes = remotes;
        this.request = request;
    }

    // Null once the publish is done, the remote configured even if its push failed; otherwise what the dialog says
    // instead, Git's answer or that the publish did not start.
    public Task<GitWriteResult?> RunAsync(Action<string>? progress = null) => remotes.Publishing.RunAsync(async _ =>
        await remotes.PublishAsync(request, progress) is not { } result ? GitWriteResult.Rejected(Failures.PublishNotStarted)
        : request.Publish.IsDone(result) ? null : result);
}
