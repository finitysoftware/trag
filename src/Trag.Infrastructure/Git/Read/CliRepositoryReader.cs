using Trag.Application.Ports.Git;
using Trag.Domain.Status;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Read;

// The repository reader the shell holds. It answers both ports from one object, because the live change stream asks the
// reader it already has whether it can also read single paths.
public sealed class CliRepositoryReader : IRepositoryReader, IIncrementalRepositoryReader
{
    private readonly RepositorySnapshotReader snapshots;
    private readonly IncrementalStatusReader increments;

    public CliRepositoryReader(GitProcessRunner runner)
    {
        var git = new PresetGitRunner(runner, LocalGitPreset.Reading);
        snapshots = new(git, new HeadReader(runner), new(git), new(git));
        increments = new(git, snapshots);
    }

    public Task<RepositorySnapshot> ReadAsync(string path, CancellationToken ct) =>
        snapshots.ReadAsync(path, ct);

    public Task<RepositorySnapshot> ReadPathsAsync(RepositorySnapshot previous, IReadOnlyList<string> paths, CancellationToken ct) =>
        increments.ReadPathsAsync(previous, paths, ct);
}
