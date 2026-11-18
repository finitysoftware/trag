using Trag.Application.Live;
using Trag.Application.Ports.Workspace;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Tags;
using Trag.Domain.Writes;

namespace Trag.Application.Tags;

// The repository's tags: the list, which follows the tags' fingerprint in the snapshot (it reads when a tag came, went
// or moved, by Trag or any other program, and empties when the last one is gone); creating one at a commit of the
// history, pushing one to a remote and deleting one, each through the repository's writer.
public sealed class TagsModel
{
    private readonly Repository repository;

    public TagsModel(Repository repository, IBranchWorkspace? reader)
    {
        this.repository = repository;
        List = new(repository, reader is null ? null : reader.ReadTagsAsync, empty: [],
            key: snapshot => snapshot.TagRefs.Length == 0 ? null : snapshot.TagRefs) { FailedMessage = Failures.TagsNotRead };
    }

    public Repository Repository => repository;
    public Loader<IReadOnlyList<TagInfo>> List { get; }
    // The snapshot says whether there is a tag before the list arrives.
    public bool HasTags => repository.Snapshot is { TagRefs.Length: > 0 };
    // The remote a new tag is offered to: origin, or the first one listed; null without a remote.
    public string? PushRemote => repository.Snapshot?.PreferredRemote?.Name;
    public bool SignsTags => repository.Snapshot?.SignsTags == true;

    // A tag created and, when asked, pushed right after. The answer is the creation's: the tag exists whatever the push
    // says, and the push says it in the status bar and the banner like any write.
    public async Task<GitWriteResult?> CreateAsync(TagOperation tag, string? pushTo)
    {
        var created = await repository.Writer.RunAsync(expected => new TagRequest(expected, tag));
        if (created is { Outcome: GitWriteOutcome.Succeeded } && pushTo is not null) await PushAsync(tag.Name.Trim(), pushTo);
        return created;
    }

    // A tag pushed to the remote tags go to; nothing without one.
    public Task<GitWriteResult?> PushAsync(string name) => PushRemote is { } remote ? PushAsync(name, remote) : RepositoryWriter.NotRun;

    private Task<GitWriteResult?> PushAsync(string name, string remote) =>
        repository.Writer.RunAsync(expected => new RemoteTagRequest(expected, remote, name, RemoteTagAction.Push));

    // A tag deleted here and, when asked, on the remote tags go to first: a tag the remote kept is still here to try again.
    public async Task<GitWriteResult?> DeleteAsync(string name, bool alsoOnRemote)
    {
        if (alsoOnRemote && PushRemote is { } alsoOn)
        {
            var remote = await repository.Writer.RunAsync(expected => new RemoteTagRequest(expected, alsoOn, name, RemoteTagAction.Delete));
            if (remote is not { Outcome: GitWriteOutcome.Succeeded }) return remote;
        }
        return await repository.Writer.RunAsync(expected => new TagDeleteRequest(expected, name));
    }
}
