using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Setup;
using Trag.Domain.Presentation.Text;

namespace Trag.ViewModels.Setup;

// Settings -> Git over the settings model (GitSettingsModel): what runs, where it was found and what it brings, the
// git.exe the user chooses or gives up, and why. Settings lets the shell's Git go when it closes.
public sealed partial class GitSettingsViewModel : ObservableObject
{
    private readonly GitSettingsModel settings;

    public GitSettingsViewModel(GitSettingsModel settings)
    {
        this.settings = settings;
        settings.PropertyChanged += (_, e) => OnPropertyChanged(e);
    }

    public IReadOnlyList<DetailFact> Facts => settings.Facts;
    public string Message => settings.Message;
    public bool IsChosen => settings.IsChosen;

    public void Detach() => settings.Detach();
    public Task LoadAsync() => settings.LoadAsync();
    public Task ChooseAsync(string path) => settings.ChooseAsync(path);
    [RelayCommand] private Task ForgetChoiceAsync() => settings.ForgetChoiceAsync();
}
