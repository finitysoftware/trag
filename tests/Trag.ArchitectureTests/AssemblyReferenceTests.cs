using System.Reflection;

namespace Trag.ArchitectureTests;

// The dependency direction: Domain depends on nothing; Application on Domain and the MVVM toolkit;
// Infrastructure and ViewModels on Application and Domain and never on each other; ViewModels never sees Avalonia. Desktop
// may reference everything, but only its composition root may use Infrastructure (source rule).
public sealed class AssemblyReferenceTests
{
    private static string[] References(Assembly assembly) => assembly.GetReferencedAssemblies().Select(name => name.Name!).OrderBy(name => name, StringComparer.Ordinal).ToArray();
    private static string[] OurAssemblies(string[] references) => references.Where(name => name.StartsWith("Trag.", StringComparison.Ordinal)).ToArray();

    [Fact]
    public void DomainReferencesNoOtherAssembly()
    {
        var references = References(typeof(Trag.Domain.Paths.GitPath).Assembly);
        Assert.Empty(OurAssemblies(references));
        Assert.DoesNotContain(references, name => name.StartsWith("Avalonia", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("CommunityToolkit", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplicationReferencesOnlyDomainAndTheMvvmToolkit()
    {
        // The live model is observable: the toolkit is Application's one extra dependency.
        var references = References(typeof(Trag.Application.Live.Repository).Assembly);
        Assert.Equal(["Trag.Domain"], OurAssemblies(references));
        Assert.DoesNotContain(references, name => name.StartsWith("Avalonia", StringComparison.Ordinal));
        Assert.Contains("CommunityToolkit.Mvvm", references);
    }

    [Fact]
    public void InfrastructureReferencesOnlyApplicationAndDomainAmongTragAssemblies()
    {
        var references = References(typeof(Trag.Infrastructure.Git.Processes.GitProcessRunner).Assembly);
        Assert.Equal(["Trag.Application", "Trag.Domain"], OurAssemblies(references));
        Assert.DoesNotContain(references, name => name.StartsWith("Avalonia", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("CommunityToolkit", StringComparison.Ordinal));
    }

    [Fact]
    public void ViewModelsReferenceOnlyApplicationDomainAndTheMvvmToolkit()
    {
        var references = References(typeof(Trag.ViewModels.Shell.ShellViewModel).Assembly);
        Assert.Equal(["Trag.Application", "Trag.Domain"], OurAssemblies(references));
        Assert.DoesNotContain(references, name => name.StartsWith("Avalonia", StringComparison.Ordinal));
        Assert.Contains("CommunityToolkit.Mvvm", references);
    }

    [Fact]
    public void DesktopIsTheOnlyAssemblyThatReferencesAvalonia()
    {
        var references = References(typeof(Trag.Desktop.Program).Assembly);
        Assert.Contains(references, name => name.StartsWith("Avalonia", StringComparison.Ordinal));
        Assert.Contains("Trag.Application", references); Assert.Contains("Trag.ViewModels", references);
    }
}
