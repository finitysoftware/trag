using Trag.Infrastructure.Accounts.Credentials;

namespace Trag.Desktop.Composition;

// The Git askpass helper's entry: the protocol and its checks are AskPassClient's. Runs before anything that could touch
// Avalonia.
internal static class AskPassEntryPoint
{
    public static int Run(string[] args) => AskPassClient.RunFromEnvironment(Environment.GetEnvironmentVariable, args, Console.OpenStandardOutput());
}
