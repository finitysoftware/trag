using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace Trag.Desktop.Dialogs.Settings;

public sealed partial class DeviceLoginDialog : DialogHost
{
    public DeviceLoginDialog() => InitializeComponent();

    private void CopyCode(object? sender, RoutedEventArgs e) => _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(DeviceCode.Text ?? "");
}
