using System.Reflection;

namespace Trag.Desktop.Platform;

// The version the build stamped on Trag, with the commit it was built from after "+".
public static class BuildVersion
{
    public static string Informational { get; } =
        typeof(BuildVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
}
