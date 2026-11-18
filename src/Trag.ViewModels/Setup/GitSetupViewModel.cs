using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Setup;
using Trag.Domain.Presentation.Text;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.Setup;

// The Git dialog of the window: open while there is no Git to run, saying why; it sends the user to Git's download page,
// takes the git.exe the user chooses, and while there is no Git looks again whenever the user comes back to the window,
// as after installing Git, closed or not. Closed by the user, it lets the shell show what it can without Git.
public sealed partial class GitSetupViewModel : ObservableObject
{
    private readonly GitSetup setup;

    public GitSetupViewModel(GitSetup setup)
    {
        this.setup = setup;
        setup.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(GitSetup.Finding)) Show(); };
    }

    public GitSetup Setup => setup;
    public Func<IShellHost?> Host { get; set; } = () => null;
    [ObservableProperty] public partial bool IsOpen { get; private set; }
    public string Text => GitSetupText.Problem(setup.Finding);

    public Task<bool> StartAsync(CancellationToken ct) => setup.StartAsync(ct);
    public Task ActivatedAsync() => setup.ActivatedAsync();

    public event Action? Dismissed;

    [RelayCommand] private void InstallGit() => Host()?.OpenBrowser(GitSetupText.DownloadPage);

    [RelayCommand]
    private async Task ChooseGitAsync()
    {
        if (Host() is { } host && await host.PickGitExecutableAsync() is { } path) await setup.ChooseAsync(path, CancellationToken.None);
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        Dismissed?.Invoke();
    }

    private void Show()
    {
        IsOpen = !setup.Finding.Runs;
        OnPropertyChanged(nameof(Text));
    }
}
