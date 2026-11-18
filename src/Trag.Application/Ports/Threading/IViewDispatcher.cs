namespace Trag.Application.Ports.Threading;

// Runs an action on the thread the views live on; the live model reports progress and applies chunks through it.
public interface IViewDispatcher { Task InvokeAsync(Action action); }
