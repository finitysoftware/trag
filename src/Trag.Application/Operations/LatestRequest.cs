namespace Trag.Application.Operations;

// One in-flight request per feature: beginning a new one cancels the previous, and a scope taken before an await tells
// the continuation whether it still speaks for the latest request. Dispose the scope in the finally of the operation.
// A request ends in one of two ways. Cancel supersedes it: a newer generation begins and the operation stays quiet.
// Interrupt only cancels its token: the operation keeps its ticket, reports the interruption and clears its own flags.
public sealed class LatestRequest
{
    private long generation;
    private CancellationTokenSource? request;

    public bool IsActive => request is not null;
    public long Generation => generation;

    public Scope Begin(CancellationToken ct = default)
    {
        var ticket = ++generation;
        var previous = request;
        request = null;
        previous?.Cancel();
        var source = ct.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(ct) : new CancellationTokenSource();
        request = source;
        return new Scope(this, ticket, source);
    }

    public void Cancel()
    {
        generation++;
        var previous = request;
        request = null;
        previous?.Cancel();
    }

    public void Interrupt() => request?.Cancel();

    public bool IsCurrent(long ticket) => ticket == generation;

    public sealed class Scope(LatestRequest owner, long ticket, CancellationTokenSource source) : IDisposable
    {
        public long Ticket => ticket;
        public CancellationToken Token => source.Token;
        // No newer request began: an interrupted request is still the latest and reports its own outcome.
        public bool IsLatest => owner.IsCurrent(ticket);
        // Still wanted: the latest one and not cancelled; only then does a result reach the screen.
        public bool IsCurrent => owner.IsCurrent(ticket) && !source.IsCancellationRequested;
        public void Dispose()
        {
            if (ReferenceEquals(owner.request, source)) owner.request = null;
            source.Dispose();
        }
    }
}
