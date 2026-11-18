using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Setup;

namespace Trag.Application.Setup;

// Settings -> Git without a screen: the Git Trag runs, where it was found and, once read, what it brings, and whether the
// user chose it. The user can choose another git.exe, told why when it does not run, and give a chosen one up so Trag
// finds Git itself again, told so when Trag finds no other. Without a Git to run it says why, as the window's dialog
// does. Settings lets the shell's Git go when it closes.
public sealed partial class GitSettingsModel : ObservableObject
{
    private readonly GitSetup setup;
    private GitFeatures? features;

    public GitSettingsModel(GitSetup setup)
    {
        this.setup = setup;
        setup.PropertyChanged += Follow;
        Show();
    }

    [ObservableProperty] public partial IReadOnlyList<DetailFact> Facts { get; private set; } = [];
    [ObservableProperty] public partial string Message { get; private set; } = "";
    [ObservableProperty] public partial bool IsChosen { get; private set; }

    public void Detach() => setup.PropertyChanged -= Follow;

    public async Task LoadAsync()
    {
        features = await setup.DescribeAsync(CancellationToken.None);
        Show();
    }

    public async Task ChooseAsync(string path)
    {
        Message = GitSetupText.Problem(await setup.ChooseAsync(path, CancellationToken.None));
        await LoadAsync();
    }

    public async Task ForgetChoiceAsync()
    {
        Message = GitSetupText.Forgotten(await setup.ForgetChoiceAsync(CancellationToken.None));
        await LoadAsync();
    }

    private void Follow(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GitSetup.Finding)) Show();
    }

    private void Show()
    {
        var running = setup.Finding.Running;
        Facts = running is null ? [] : GitSetupText.Facts(running, features);
        IsChosen = running?.Origin == GitOrigin.Chosen;
        if (running is null) Message = GitSetupText.Problem(setup.Finding);
    }
}
