using System.Text;

namespace Trag.ArchitectureTests;

// A stray non-UTF-8 byte renders as a replacement character in the UI (MainWindow.axaml once carried a bare 0xB7).
public sealed class SourceEncodingTests
{
    [Fact]
    public void EverySourceAndXamlFileIsValidUtf8WithoutAByteOrderMark()
    {
        var strict = new UTF8Encoding(false, true);
        var violations = new List<string>();
        foreach (var project in RepositoryRoot.ProductionProjects)
        {
            var root = RepositoryRoot.Project(project);
            var separator = Path.DirectorySeparatorChar;
            foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(file => file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".axaml", StringComparison.Ordinal))
                .Where(file => !file.Contains(separator + "obj" + separator, StringComparison.Ordinal) && !file.Contains(separator + "bin" + separator, StringComparison.Ordinal)))
            {
                var bytes = File.ReadAllBytes(file);
                if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) { violations.Add(file + ": byte order mark"); continue; }
                try { strict.GetString(bytes); }
                catch (DecoderFallbackException) { violations.Add(file + ": not valid UTF-8"); }
            }
        }
        Assert.Empty(violations);
    }
}
