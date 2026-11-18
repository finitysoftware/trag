using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Operations;

namespace Trag.Application.Live;

// One user operation that is not a read: busy while it runs, one at a time, cancellable by its owner; Completion lets a
// test wait. The work receives the token and an Application service inside it turns every
// failure into an outcome, so the block has no catch. Its owner is the model whose flow it runs; a view model forwards
// IsBusy to the commands and the view.
public sealed partial class Operation : ObservableObject
{
    private readonly LatestRequest request = new();
    private CancellationToken running;

    [ObservableProperty] public partial bool IsBusy { get; private set; }
    public Task Completion { get; private set; } = Task.CompletedTask;

    // Starts the work unless one is running, which refuses the start at once; the started task is also Completion.
    public Task RunAsync(Func<CancellationToken, Task> work) => IsBusy ? Task.CompletedTask : Completion = RunCoreAsync(work);

    // The same for work that answers: a refused start answers null.
    public async Task<T?> RunAsync<T>(Func<CancellationToken, Task<T?>> work) where T : class
    {
        T? answer = null;
        await RunAsync(async ct => { answer = await work(ct); });
        return answer;
    }

    // Whether the work that was handed this token is the one running: late progress of an ended work is dropped.
    public bool Runs(CancellationToken token) => IsBusy && running == token && !token.IsCancellationRequested;

    // Cancels the running work's token; the work says how it ended.
    public void Cancel() => request.Interrupt();

    private async Task RunCoreAsync(Func<CancellationToken, Task> work)
    {
        using var scope = request.Begin();
        running = scope.Token;
        IsBusy = true;
        try { await work(scope.Token); }
        finally { if (scope.IsLatest) IsBusy = false; }
    }
}
