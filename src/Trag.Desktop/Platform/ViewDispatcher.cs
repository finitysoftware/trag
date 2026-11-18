using Avalonia.Threading;
using Trag.Application.Ports.Threading;

namespace Trag.Desktop.Platform;

public sealed class ViewDispatcher : IViewDispatcher
{
    public async Task InvokeAsync(Action action) => await Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Background);
}
