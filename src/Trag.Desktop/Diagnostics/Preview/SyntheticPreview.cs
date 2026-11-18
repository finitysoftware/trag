using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.Presentation.Text;
using Trag.ViewModels.Shell;

namespace Trag.Desktop.Diagnostics.Preview;

// The virtualization fixture behind --preview and the "preview" probe command: 10,000 synthetic files and 50,000 diff
// lines shown in the section's place while the shell's Preview is this object.
internal sealed partial class SyntheticPreview(ShellViewModel shell) : ObservableObject
{
    [ObservableProperty] public partial PreviewList Files { get; set; } = new(0, _ => throw new InvalidOperationException());
    [ObservableProperty] public partial PreviewList DiffLines { get; set; } = new(0, _ => throw new InvalidOperationException());
    [ObservableProperty] public partial PreviewRow? SelectedFile { get; set; }

    public bool IsShown => ReferenceEquals(shell.Preview, this);

    public void Toggle()
    {
        var on = !IsShown;
        shell.Preview = on ? this : null;
        SelectedFile = null;
        Files = new(on ? 10_000 : 0, index => new(index + 1, $"sample/folder-{index / 100:D3}/file-{index:D5}.cs"));
        DiffLines = new(on ? 50_000 : 0, index => new(index + 1, $"+  // Synthetic diff line {index + 1}: virtualization fixture"));
        shell.StatusBar.Status = on ? "SYNTHETIC DATA · 10,000 files / 50,000 diff lines" : SessionText.Ready;
    }
}
