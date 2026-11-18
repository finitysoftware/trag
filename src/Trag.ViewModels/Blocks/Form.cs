using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.Writes;

namespace Trag.ViewModels.Blocks;

// What every dialog with input shares: it is open or not, it can carry a notice, and submitting it runs one write that
// closes and clears it once the write started or leaves it open with the reason. The fields themselves belong to the derived draft.
public partial class Form : ObservableObject
{
    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNotice))] public partial string Notice { get; set; } = "";
    public bool HasNotice => Notice.Length != 0;

    public void Open()
    {
        Reset();
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        Notice = "";
    }

    public void Reset()
    {
        Close();
        ResetFields();
    }

    protected virtual void ResetFields() { }

    // A write that started closes and clears the form; anything else keeps it open with Git's reason, or the given one
    // when there is none.
    public async Task<GitWriteResult?> SubmitAsync(Func<Task<GitWriteResult?>> write, string failure)
    {
        Notice = "";
        var result = await write();
        if (result is { Started: true }) Reset();
        else Notice = result?.Message ?? failure;
        return result;
    }
}
