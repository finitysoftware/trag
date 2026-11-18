using System.Globalization;
using Trag.Desktop.Diagnostics.Probe;
using Trag.Desktop.Diagnostics.Probe.Commands;

namespace Trag.Desktop.UnitTests.Diagnostics;

public sealed class ProbeCommandLoopTests
{
    [Theory]
    [InlineData("quit", "quit", "")]
    [InlineData("open D:\\repos\\my project", "open", "D:\\repos\\my project")]
    [InlineData("stash-create half-done login form", "stash-create", "half-done login form")]
    [InlineData("select unstaged 0", "select", "unstaged 0")]
    [InlineData("diff-ui state", "diff-ui", "state")]
    public void WhenACommandLineIsRead_ShouldNameTheCommandByItsFirstWordAndKeepTheRestVerbatim(string line, string name, string rest) =>
        Assert.Equal((name, rest), ProbeCommandLoop.Tokenize(line));

    [Fact]
    public void WhenAnArgumentIsMalformed_ShouldAnswerAnErrorLineAndKeepRunning()
    {
        var loop = new ProbeCommandLoop();
        var answered = new List<string>();
        loop.Add("search", rest => answered.Add(ProbeContext.Decode(rest)));
        loop.Add("history-select", rest => answered.Add(ProbeContext.Int(rest).ToString(CultureInfo.InvariantCulture)));
        var output = new StringWriter();
        var quit = false;

        loop.Run(new StringReader("search not base64!\nhistory-select first\nsearch Zml4\nhistory-select 2\nquit\n"), output, () => quit = true);

        Assert.Equal(["TRAG_PROBE_ERROR search", "TRAG_PROBE_ERROR history-select"], output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(["fix", "2"], answered);
        Assert.True(quit);
    }

    // The real theme rule throws on a name it does not know; the loop answers that as it answers a malformed argument.
    [Fact]
    public void WhenAThemeNameIsUnknown_ShouldAnswerAnErrorLineInsteadOfEndingTheProbe()
    {
        var loop = new ProbeCommandLoop();
        loop.Add("theme", rest => LifecycleCommands.Theme(rest));
        var output = new StringWriter();
        var quit = false;

        loop.Run(new StringReader("theme sepia\ntheme dark\nquit\n"), output, () => quit = true);

        Assert.Equal(["TRAG_PROBE_ERROR theme"], output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(quit);
    }
}
