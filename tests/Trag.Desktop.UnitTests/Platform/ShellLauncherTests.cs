using System.ComponentModel;
using System.Diagnostics;
using Trag.Desktop.Platform;

namespace Trag.Desktop.UnitTests.Platform;

// The launcher is handed what starts a process, so no test opens a terminal, a file manager or a browser.
public sealed class ShellLauncherTests
{
    private readonly List<ProcessStartInfo> started = [];

    [Theory]
    [InlineData("http://github.com/login/device")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    public void WhenAnAddressIsNotHttps_ShouldNotReachTheBrowser(string address)
    {
        Assert.False(new ShellLauncher(Start).OpenBrowser(new Uri(address)));
        Assert.Empty(started);
    }

    // A program that is not there, as a desktop without x-terminal-emulator or xdg-open has, or a folder that went.
    [Fact]
    public void WhenTheTerminalOrRevealCannotStart_ShouldSaySo()
    {
        var launcher = new ShellLauncher(_ => throw new Win32Exception(2));
        var folder = Path.GetTempPath(); var file = Path.Combine(folder, "a.txt");
        Assert.False(launcher.OpenTerminal(folder));
        Assert.False(launcher.Reveal(folder));
        Assert.False(launcher.RevealFile(file));
        Assert.False(launcher.Open(file));
        Assert.False(launcher.OpenBrowser(new Uri("https://example.com/")));
    }

    [Fact]
    public void WhenTheSystemStartsWhatItIsAskedFor_ShouldSayItStarted()
    {
        var launcher = new ShellLauncher(Start);
        var folder = Path.GetTempPath(); var file = Path.Combine(folder, "a.txt");
        Assert.True(launcher.OpenTerminal(folder));
        Assert.True(launcher.Reveal(folder));
        Assert.True(launcher.RevealFile(file));
        Assert.True(launcher.Open(file));
        Assert.True(launcher.OpenBrowser(new Uri("https://example.com/")));
        Assert.Equal(5, started.Count);
    }

    // As Process.Start answers for a document an already running program took.
    private Process? Start(ProcessStartInfo start)
    {
        started.Add(start);
        return null;
    }
}
