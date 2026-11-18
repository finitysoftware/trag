using System.Reflection;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.ViewModels.Shell;

namespace Trag.ArchitectureTests;

public sealed class InheritanceTests
{
    private static readonly Assembly[] ProductionAssemblies =
    [
        typeof(GitWriteRequest).Assembly,
        typeof(CliFileDiffReader).Assembly,
        typeof(ShellViewModel).Assembly,
        typeof(Trag.Desktop.Program).Assembly
    ];

    [Fact]
    public void ProductionCodeHasAtMostOneAbstractClass()
    {
        // Abstract records are classes too; static classes are abstract and sealed in metadata.
        var abstractClasses = ProductionAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsClass && type.IsAbstract && !type.IsSealed)
            .ToArray();

        Assert.True(abstractClasses.Length <= 1,
            "Prefer composition; abstract production classes: " + string.Join(", ", abstractClasses.Select(type => type.FullName)));
    }

    [Fact]
    public void ProductionClassesHaveAtMostOneOwnBaseClass()
    {
        var violations = ProductionAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => IsProductionClass(type.BaseType) && IsProductionClass(type.BaseType?.BaseType))
            .Select(type => type.FullName)
            .ToArray();

        Assert.Empty(violations);
    }

    private static bool IsProductionClass(Type? type) =>
        type is { IsClass: true } && ProductionAssemblies.Contains(type.Assembly);
}
