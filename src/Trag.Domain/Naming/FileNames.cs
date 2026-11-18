using Trag.Domain.Paths;

namespace Trag.Domain.Naming;

// What a file's name says about opening it: programs, scripts, shortcuts, registry files, packages that install, help
// files and add-ins run instead of showing. Git for Windows and Python register their scripts to run when opened.
public static class FileNames
{
    private static readonly HashSet<string> Running = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".pif", ".cpl", ".msi", ".msp", ".msc", ".bat", ".cmd", ".ps1", ".psm1", ".ps1xml", ".psc1",
        ".vb", ".vbs", ".vbe", ".js", ".jse", ".ws", ".wsc", ".wsf", ".wsh", ".hta", ".jar", ".jnlp", ".lnk", ".url", ".scf",
        ".reg", ".inf", ".application", ".appref-ms", ".settingcontent-ms", ".gadget", ".diagcab", ".chm", ".xll",
        ".msix", ".msixbundle", ".appx", ".appxbundle", ".appinstaller", ".msu", ".sh", ".bash", ".py", ".pyw", ".pyz", ".pyzw",
        ".pyc", ".pyo", ".pl", ".rb"
    };

    public static bool Runs(GitPath path) => Running.Contains(Path.GetExtension(path.Display));
}
