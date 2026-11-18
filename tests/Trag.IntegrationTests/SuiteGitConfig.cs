using System.Runtime.CompilerServices;

namespace Trag.IntegrationTests;

// Every Git the suite starts inherits this process's environment, so before any test runs the suite turns the machine's
// system configuration off and names suite.gitconfig as the global one. A run that names its own global configuration,
// as the hostile pass does, keeps it and leaves the system one to the run.
internal static class SuiteGitConfig
{
    [ModuleInitializer]
    internal static void Isolate()
    {
        if (Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL") is not null) return;
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", Path.Combine(AppContext.BaseDirectory, "suite.gitconfig"));
        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
    }
}
