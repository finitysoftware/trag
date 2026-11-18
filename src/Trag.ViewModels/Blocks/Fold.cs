using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Trag.ViewModels.Blocks;

// A list that folds under its header and opens again; it is open at first.
public sealed partial class Fold : ObservableObject
{
    [ObservableProperty] public partial bool IsOpen { get; private set; } = true;

    [RelayCommand] private void Toggle() => IsOpen = !IsOpen;
}
