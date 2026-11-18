using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Live;
using Trag.Domain.Writes;

namespace Trag.ViewModels.Blocks;

// A destructive action behind a question. Request opens it for one subject, Confirm runs the action and closes on
// success, or when Git stopped at conflicts the operation's view takes over, or shows why not, Cancel closes unless the
// action is already running. The owner closes it when the subject disappears from the list it came from. The action is a Git write with a fallback text, or anything that answers
// with a reason for staying open (null when done). Over a repository the question follows its writability and requery,
// and says its text again for each snapshot, so a branch switched under it is named; the bare form leaves all to the owner.
public sealed partial class Confirmation<T> : ObservableObject where T : class
{
    private readonly Func<T, Task<string?>> perform;
    private readonly Func<T, string> describe;
    private readonly Func<bool> allowed;

    public Confirmation(Func<T, Task<string?>> perform, Func<T, string> describe, Func<bool> allowed)
    {
        this.perform = perform;
        this.describe = describe;
        this.allowed = allowed;
    }

    public Confirmation(Repository repository, Func<T, Task<string?>> perform, Func<T, string> describe, Func<bool>? allowed = null)
        : this(perform, describe, allowed ?? (() => repository.CanWrite))
    {
        repository.Requery.Track(ConfirmCommand);
        repository.SnapshotApplied += (_, _) => OnPropertyChanged(nameof(Text));
    }

    public Confirmation(Repository repository, Func<T, Task<GitWriteResult?>> perform, Func<T, string> describe, string failure, Func<bool>? allowed = null)
        : this(repository, async subject => await perform(subject) is { } result
            ? result.Started ? null : result.Message : failure, describe, allowed) { }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsOpen), nameof(Text)), NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial T? Subject { get; private set; }
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ConfirmCommand))] public partial bool IsRunning { get; private set; }
    [ObservableProperty] public partial string Message { get; set; } = "";
    public bool IsOpen => Subject is not null;
    // The question's subtitle: what exactly is about to go.
    public string Text => Subject is { } subject ? describe(subject) : "";

    public void Request(T subject)
    {
        Message = "";
        Subject = subject;
    }

    // The subject is gone, or the repository is: nothing left to ask about.
    public void Close()
    {
        Subject = null;
        Message = "";
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!IsRunning) Close();
    }

    private bool CanConfirm() => Subject is not null && !IsRunning && allowed();
    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        if (Subject is not { } subject || !CanConfirm()) return;
        IsRunning = true;
        Message = "";
        try
        {
            var reason = await perform(subject);
            if (reason is null) Close();
            else Message = reason;
        }
        finally { IsRunning = false; }
    }
}
