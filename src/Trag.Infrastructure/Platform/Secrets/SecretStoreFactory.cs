namespace Trag.Infrastructure.Platform.Secrets;

public static class SecretStoreFactory
{
    public static ISecretStore CreatePlatformStore() => OperatingSystem.IsWindows()
        ? new WindowsSecretStore() : new LinuxSecretStore();
}
