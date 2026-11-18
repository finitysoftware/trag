using CommunityToolkit.Mvvm.Input;

namespace Trag.Application.Live;

// Avalonia has no CommandManager.RequerySuggested. This is that idea for one shell: whenever shared state changes,
// every tracked command re-evaluates, and Suggested lets a feature refresh the properties it derives from that state.
// CanExecute checks are trivial, so asking all of them costs nothing worth measuring; what it buys is that no feature
// can forget to refresh a command when a precondition it does not own changes.
public sealed class Requery
{
    private readonly List<IRelayCommand> commands = [];
    private bool invalidating;
    private bool pending;

    // Raised after the commands, for derived properties that XAML binds directly.
    public event Action? Suggested;

    public void Track(params IRelayCommand[] tracked) => commands.AddRange(tracked);

    public void Invalidate()
    {
        // A handler that changes state during a pass gets one more full pass instead of a nested one.
        if (invalidating) { pending = true; return; }
        invalidating = true;
        try
        {
            do
            {
                pending = false;
                foreach (var command in commands) command.NotifyCanExecuteChanged();
                Suggested?.Invoke();
            } while (pending);
        }
        finally { invalidating = false; }
    }
}
