using System.Text;

namespace Trag.Desktop.Diagnostics.Probe;

// Reads probe commands from stdin on a background thread and dispatches each line by its first word; the rest of the
// line is the command's argument text, verbatim. Unknown commands are ignored, "quit" ends the loop, and a command whose
// argument it cannot read is answered with TRAG_PROBE_ERROR and its name.
internal sealed class ProbeCommandLoop
{
    private readonly Dictionary<string, Action<string>> commands = new(StringComparer.Ordinal);

    public void Add(string name, Action<string> handler) => commands.Add(name, handler);
    public void Add(string name, Action handler) => commands.Add(name, _ => handler());

    public static (string Name, string Argument) Tokenize(string line)
    {
        var space = line.IndexOf(' ');
        return space < 0 ? (line, "") : (line[..space], line[(space + 1)..]);
    }

    public void Start(Action quit)
    {
        var inputThread = new Thread(() =>
        {
            using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            Run(input, Console.Out, quit);
        }) { IsBackground = true, Name = "Trag probe input" };
        inputThread.Start();
    }

    public void Run(TextReader input, TextWriter output, Action quit)
    {
        while (input.ReadLine() is { } line)
        {
            var (name, rest) = Tokenize(line);
            if (name == "quit") { quit(); break; }
            if (!commands.TryGetValue(name, out var handler)) continue;
            try { handler(rest); }
            catch (Exception) { output.WriteLine("TRAG_PROBE_ERROR " + name); }
        }
    }
}
