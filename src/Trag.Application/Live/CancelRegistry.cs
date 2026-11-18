namespace Trag.Application.Live;

// The reads and operations one Cancel reaches: each registers its busy flag and its cancellation, and says when its busy
// flag changed so the shell can offer Cancel. The application has one for what outlives a repository (the open in
// flight, the repository list, a clone); every repository has one for its own reads and its write, which go with it. An
// operation that goes before its registry does disposes its registration, so Cancel reaches it no more.
public sealed class CancelRegistry
{
    private readonly List<Registration> operations = [];

    public bool CanCancel => operations.Any(operation => operation.IsBusy());
    public int Count => operations.Count;
    public event Action? Changed;

    public IDisposable Register(Func<bool> busy, Action cancel)
    {
        var registration = new Registration(this, busy, cancel);
        operations.Add(registration);
        return registration;
    }

    public void NotifyChanged() => Changed?.Invoke();

    public void CancelAll()
    {
        foreach (var operation in operations.ToArray()) operation.Cancel();
    }

    private sealed class Registration(CancelRegistry registry, Func<bool> busy, Action cancel) : IDisposable
    {
        public bool IsBusy() => busy();
        public void Cancel() => cancel();
        public void Dispose() => registry.operations.Remove(this);
    }
}
