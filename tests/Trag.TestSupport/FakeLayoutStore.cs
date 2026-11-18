using Trag.Application.Ports.Storage;
using Trag.Domain.Presentation;

namespace Trag.TestSupport;

public sealed class FakeLayoutStore : ILayoutStore
{
    public SavedLayout Preferences { get; set; } = new();
    public SavedLayout? Saved { get; private set; }
    public int Saves { get; private set; }
    public Exception? SaveFailure { get; set; }
    // Set, the load fails with it, as a file that is not the layout's JSON would.
    public Exception? LoadFailure { get; set; }
    public AsyncGate LoadGate { get; } = new();
    public AsyncGate SaveGate { get; } = new();

    public async Task<SavedLayout> LoadLayoutAsync(CancellationToken ct)
    {
        await LoadGate.WaitAsync(ct).ConfigureAwait(false);
        if (LoadFailure is not null) throw LoadFailure;
        return Preferences;
    }

    public async Task SaveLayoutAsync(SavedLayout preferences, CancellationToken ct)
    {
        Saves++;
        await SaveGate.WaitAsync(ct).ConfigureAwait(false);
        if (SaveFailure is not null) throw SaveFailure;
        Saved = preferences;
    }
}
