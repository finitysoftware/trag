using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Trag.Desktop.Composition;

namespace Trag.Desktop;

public sealed partial class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) ShellComposition.Start(desktop, this, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        base.OnFrameworkInitializationCompleted();
    }
}
