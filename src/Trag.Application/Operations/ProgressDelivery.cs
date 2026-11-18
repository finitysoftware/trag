namespace Trag.Application.Operations;

// Progress that reaches a screen without holding up the work: each value is handed on, and a failure to show it (a window
// that is closing, a browser that would not open) never changes how the operation ends.
public static class ProgressDelivery
{
    public static IProgress<T> Of<T>(Func<T, Task> report) => new Delivery<T>(report);

    private sealed class Delivery<T>(Func<T, Task> report) : IProgress<T>
    {
        public void Report(T value) => _ = DeliverAsync(value);
        private async Task DeliverAsync(T value) { try { await report(value); } catch (Exception) { /* Showing progress cannot change the outcome. */ } }
    }
}
