using Trag.Application.Ports.Git;
using Trag.Domain.Writes;

namespace Trag.TestSupport;

public sealed class FakeGitWriter : IGitWriter
{
    public List<GitWriteRequest> Requests { get; } = [];
    public GitWriteRequest? LastRequest => Requests.Count == 0 ? null : Requests[^1];
    public GitWriteResult Result { get; set; } = new(GitWriteOutcome.Succeeded, "Done");
    // A Handle delegate wins over Result: it is how a test lets a write change what the next read answers with.
    public Func<GitWriteRequest, CancellationToken, Task<GitWriteResult>>? Handle { get; set; }
    public Exception? Failure { get; set; }
    public AsyncGate Gate { get; } = new();

    public async Task<GitWriteResult> ExecuteAsync(GitWriteRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (Failure is not null) throw Failure;
        if (Handle is not null) return await Handle(request, cancellationToken).ConfigureAwait(false);
        return Result;
    }
}
