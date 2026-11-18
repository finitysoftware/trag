using Trag.Application.Ports.Updates;
using Trag.Domain.Updates;

namespace Trag.TestSupport;

// The update service a test answers: every check records the version it asked with, waits behind the gate while the
// test holds it, then answers with Answer; null is a service Trag could not ask.
public sealed class FakeUpdateChecker : IUpdateChecker
{
    public List<string> Versions { get; } = [];
    public UpdateOffer? Answer { get; set; }
    public AsyncGate Gate { get; } = new();

    public async Task<UpdateOffer?> CheckAsync(string version)
    {
        Versions.Add(version);
        await Gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        return Answer;
    }
}
