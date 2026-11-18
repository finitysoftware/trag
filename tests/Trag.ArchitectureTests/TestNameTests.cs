using System.Text.RegularExpressions;

namespace Trag.ArchitectureTests;

// A test names one situation and what should come of it, When<Situation>_Should<Outcome>, so a failure reads as the
// promise that broke. The rules here name the rule itself and stay outside it.
public sealed partial class TestNameTests
{
    [Fact]
    public void ScannerFindsEveryTestMethodAndIgnoresCommentsAndStrings()
    {
        const string text = """
            // [Fact] public void InAComment() { }
            public sealed class Sample
            {
                [Fact]
                public void WhenA_ShouldB() { }

                [Theory]
                [InlineData(new[] { 1 }, "[Fact] public void InAString()")]
                public async Task WhenC_ShouldD(int[] values, string text) { }

                [AvaloniaFact(Timeout = 5000)]
                public async Task OldName() { }

                private static void Helper() { }
            }
            """;
        Assert.Equal(["WhenA_ShouldB", "WhenC_ShouldD", "OldName"], TestMethods(text));
    }

    [Fact]
    public void EveryTestIsNamedWhenASituationShouldAnOutcome()
    {
        var tests = RepositoryRoot.TestProjects.SelectMany(RepositoryRoot.TestSources)
            .SelectMany(file => TestMethods(file.Text).Select(name => (File: file, Name: name))).ToArray();
        Assert.All(RepositoryRoot.TestProjects, project => Assert.Contains(tests, test => test.File.Project == project));
        Assert.Empty(tests.Where(test => !Named().IsMatch(test.Name)).Select(test => test.File.Display + ": " + test.Name));
    }

    // The method under each [Fact] or [Theory] (Avalonia's too); the attributes between them may hold anything but a method.
    private static IReadOnlyList<string> TestMethods(string text)
    {
        var code = CSharpSource.Strip(text);
        return TestAttribute().Matches(code).Select(attribute => Method().Match(code, attribute.Index + attribute.Length).Groups["name"].Value).ToArray();
    }

    [GeneratedRegex(@"\[(?:Avalonia)?(?:Fact|Theory)\b", RegexOptions.CultureInvariant)]
    private static partial Regex TestAttribute();

    [GeneratedRegex(@"\b(?:Task|ValueTask|void)\s+(?<name>[A-Za-z_]\w*)\s*[(<]", RegexOptions.CultureInvariant)]
    private static partial Regex Method();

    [GeneratedRegex(@"^When[A-Z][A-Za-z0-9]*_Should[A-Z][A-Za-z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Named();
}
