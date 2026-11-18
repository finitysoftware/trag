using Trag.Application.Ports.Git;
using Trag.Domain.Stash;

namespace Trag.TestSupport;

public sealed class FakeStashReader : IStashReader
{
    public List<StashEntry> Entries { get; } = [];
    public int Calls { get; private set; }
    public AsyncGate Gate { get; } = new();

    // Inserts at the top like git stash push and renumbers the indices.
    public StashEntry Add(string message, DateTimeOffset? when = null)
    {
        var id = Snapshots.Id((char)('c' + Entries.Count));
        Entries.Insert(0, new(id, 0, message, StashEntry.BranchOf(message), when ?? new DateTimeOffset(2026, 9, 14, 21, 31, 0, TimeSpan.Zero)));
        Renumber();
        return Entries[0];
    }

    public void Remove(string id) { Entries.RemoveAll(entry => entry.Id == id); Renumber(); }
    public string? Head => Entries.Count == 0 ? null : Entries[0].Id;

    private void Renumber()
    {
        for (var i = 0; i < Entries.Count; i++) Entries[i] = Entries[i] with { Index = i };
    }

    public async Task<IReadOnlyList<StashEntry>> ReadStashesAsync(string root, CancellationToken ct)
    {
        Calls++;
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        return Entries.ToArray();
    }
}
