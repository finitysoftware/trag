namespace Trag.Infrastructure.UnitTests;

// Tests that set a variable in this process's own environment run alone, after the rest: every Git another test starts
// meanwhile would inherit the variable, and Trag's readers of the environment would see it.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
    public const string Name = "Process environment";
}
