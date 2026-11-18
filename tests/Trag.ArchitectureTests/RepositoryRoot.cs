namespace Trag.ArchitectureTests;

// Source-level rules read the checked-out tree; the root is the folder holding Trag.sln above the test binaries.
internal static class RepositoryRoot
{
    public static readonly string Path = Locate();
    public static readonly string[] ProductionProjects = ["Trag.Domain", "Trag.Application", "Trag.Infrastructure", "Trag.ViewModels", "Trag.Desktop"];
    public static readonly string[] TestProjects = ["Trag.Domain.UnitTests", "Trag.Application.UnitTests", "Trag.Infrastructure.UnitTests", "Trag.ViewModels.UnitTests", "Trag.Desktop.UnitTests", "Trag.IntegrationTests"];

    private static string Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "Trag.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Trag.sln was not found above " + AppContext.BaseDirectory);
    }

    public static string Project(string name) => System.IO.Path.Combine(Path, "src", name);

    public static IEnumerable<SourceFile> ProductionSources(string project) => Sources(project, Project(project));

    public static IEnumerable<SourceFile> TestSources(string project) => Sources(project, System.IO.Path.Combine(Path, "tests", project));

    private static IEnumerable<SourceFile> Sources(string project, string root)
    {
        var separator = System.IO.Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(separator + "obj" + separator, StringComparison.Ordinal) && !file.Contains(separator + "bin" + separator, StringComparison.Ordinal))
            .OrderBy(file => file, StringComparer.Ordinal)
            .Select(file => new SourceFile(project, System.IO.Path.GetRelativePath(root, file).Replace('\\', '/'), File.ReadAllText(file)));
    }

    public static IEnumerable<SourceFile> AllProductionSources() => ProductionProjects.SelectMany(ProductionSources);
}

internal sealed record SourceFile(string Project, string RelativePath, string Text)
{
    public string Display => Project + "/" + RelativePath;
    public string FileName => System.IO.Path.GetFileName(RelativePath);
    // "MainWindow.axaml.cs" -> "MainWindow"; "Foo.cs" -> "Foo"; a partial part "Foo.Part.cs" -> "Foo".
    public string TypeNameFromFile => FileName[..FileName.IndexOf('.')];
    public bool IsXamlCodeBehind => FileName.EndsWith(".axaml.cs", StringComparison.Ordinal);
    public int LineCount => Text.Count(c => c == '\n') + (Text.Length != 0 && !Text.EndsWith('\n') ? 1 : 0);
}
