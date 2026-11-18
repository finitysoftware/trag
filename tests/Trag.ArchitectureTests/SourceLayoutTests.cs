using Xunit.Abstractions;

namespace Trag.ArchitectureTests;

public sealed class SourceLayoutTests(ITestOutputHelper output)
{
    [Fact]
    public void ScannerFindsTopLevelTypesAndIgnoresNestedOnesCommentsAndStrings()
    {
        const string text = """"
            // class NotAType
            namespace Sample;
            public sealed partial class Outer : Base
            {
                private sealed class Nested { }
                private const string Text = "class InString { }";
                private const string Raw = """
                    struct InRaw
                    """;
                public void M<T>() where T : class { }
            }
            internal record struct Pair(int A, int B);
            public interface IPort { }
            enum Kind { A, B }
            """";
        var types = CSharpSource.TopLevelTypes(text);
        Assert.Equal(["Outer", "Pair", "IPort", "Kind"], types.Select(type => type.Name));
        Assert.True(types[0].IsPartial); Assert.False(types[1].IsPartial);
        Assert.Equal("record struct", types[1].Kind);
    }

    [Fact]
    public void ScannerHandlesBlockScopedNamespaces()
    {
        const string text = "namespace A.B\n{\n    public class First { class Inner { } }\n    public class Second { }\n}\n";
        Assert.Equal(["First", "Second"], CSharpSource.TopLevelTypes(text).Select(type => type.Name));
    }

    [Fact]
    public void EveryProductionFileDeclaresExactlyOneTopLevelTypeNamedLikeTheFile()
    {
        var violations = new List<string>();
        foreach (var file in RepositoryRoot.AllProductionSources())
        {
            var types = CSharpSource.TopLevelTypes(file.Text);
            if (types.Count != 1) violations.Add($"{file.Display}: {types.Count} top-level types ({string.Join(", ", types.Select(type => type.Name))})");
            else if (types[0].Name != file.TypeNameFromFile) violations.Add($"{file.Display}: declares {types[0].Name}");
        }
        Assert.Empty(violations);
    }

    [Fact]
    public void PartialTypesAppearOnlyWhereACompilerGeneratesTheOtherHalf()
    {
        // Allowed: XAML code-behind, System.Text.Json contexts, P/Invoke and regex holders, and MVVM-generated observable classes
        // that live in a single file. Everything else must be a whole type in one file.
        var partials = RepositoryRoot.AllProductionSources()
            .SelectMany(file => CSharpSource.TopLevelTypes(file.Text).Where(type => type.IsPartial).Select(type => (file, type))).ToArray();
        var perName = partials.GroupBy(item => item.type.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var violations = new List<string>();
        foreach (var (file, type) in partials)
        {
            if (file.IsXamlCodeBehind || type.Name.EndsWith("JsonContext", StringComparison.Ordinal)) continue;
            if (file.Text.Contains("[LibraryImport", StringComparison.Ordinal) || file.Text.Contains("[GeneratedRegex", StringComparison.Ordinal)) continue;
            var generated = file.Text.Contains("[ObservableProperty", StringComparison.Ordinal) || file.Text.Contains("[RelayCommand", StringComparison.Ordinal);
            if (generated && perName[type.Name] == 1) continue;
            violations.Add($"{file.Display}: partial {type.Kind} {type.Name}" + (perName[type.Name] > 1 ? $" spread over {perName[type.Name]} files" : ""));
        }
        Assert.Empty(violations);
    }

    [Fact]
    public void ProductionFileSizesAreReportedForReview()
    {
        // Length prompts a responsibility review; it must not encourage compressed statements.
        var files = RepositoryRoot.AllProductionSources()
            .OrderByDescending(file => file.LineCount)
            .ThenBy(file => file.Display, StringComparer.Ordinal)
            .ToArray();

        output.WriteLine($"{files.Length} production files; {files.Count(file => file.LineCount > 300)} exceed 300 lines.");
        output.WriteLine("Review the largest files and every file above 300 lines:");
        foreach (var file in files.Take(Math.Max(10, files.Count(file => file.LineCount > 300))))
            output.WriteLine($"{file.Display}: {file.LineCount} lines");
    }

    [Fact]
    public void DesktopUsesInfrastructureOnlyFromCompositionAndDiagnostics()
    {
        var violations = RepositoryRoot.ProductionSources("Trag.Desktop")
            .Where(file => !file.RelativePath.StartsWith("Composition/", StringComparison.Ordinal) && !file.RelativePath.StartsWith("Diagnostics/", StringComparison.Ordinal))
            .Where(file => CSharpSource.Strip(file.Text).Contains("Trag.Infrastructure", StringComparison.Ordinal))
            .Select(file => file.Display).ToArray();
        Assert.Empty(violations);
    }

    [Fact]
    public void RowViewModelsDoNotReferenceTheShell()
    {
        string[] rows = ["BranchRow", "StashRow", "LocalRepositoryRow", "RemoteRepositoryRow", "AccountGroup"];
        var violations = RepositoryRoot.ProductionSources("Trag.ViewModels")
            .Where(file => CSharpSource.TopLevelTypes(file.Text).Any(type => rows.Contains(type.Name, StringComparer.Ordinal)))
            .Where(file => CSharpSource.Strip(file.Text).Contains("ShellViewModel", StringComparison.Ordinal))
            .Select(file => file.Display).ToArray();
        Assert.Empty(violations);
    }
}
