using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.Accounts;

namespace Trag.ViewModels.Accounts;

// The GitHub device sign-in as the settings show it: the code to type on GitHub's page, and that page, opened once per
// sign-in.
public sealed partial class DeviceLoginPrompt : ObservableObject
{
    private bool opened;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasCode))] public partial string UserCode { get; private set; } = "";
    public bool HasCode => UserCode.Length != 0;

    public Uri? Apply(DeviceLoginProgress progress)
    {
        if (progress.UserCode.Length != 0)
            UserCode = progress.UserCode;
        if (opened || progress.VerificationUri is null)
            return null;
        opened = true;
        return progress.VerificationUri;
    }

    public void Clear()
    {
        UserCode = "";
        opened = false;
    }
}
