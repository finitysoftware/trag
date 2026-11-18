namespace Trag.IntegrationTests;

// Tests that set a variable in this process's own environment run alone, after the rest: every Git another test starts
// meanwhile would inherit the variable.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
    public const string Name = "Process environment";
}

// Variables set in this process's environment until disposed, then put back as they were.
internal sealed class EnvironmentVariables : IDisposable
{
    private readonly (string Key, string? Value)[] previous;

    public EnvironmentVariables(params (string Key, string Value)[] variables)
    {
        previous = [.. variables.Select(variable => (variable.Key, Environment.GetEnvironmentVariable(variable.Key)))];
        foreach (var (key, value) in variables) Environment.SetEnvironmentVariable(key, value);
    }

    public void Dispose()
    {
        foreach (var (key, value) in previous) Environment.SetEnvironmentVariable(key, value);
    }
}
