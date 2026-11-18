using System.Diagnostics;

if (Environment.GetEnvironmentVariable("TRAG_GITSTUB_FLOOD") is { } flood) return await FloodAsync(int.Parse(flood));

// A Git that changes nothing, for the tests of what Trag reports when Git exits 0 without doing what it was asked: every
// command that only reads goes to the Git on PATH, every command that writes ends at once with exit 0 and no output.
// With TRAG_GIT_STUB=push it also skips every push, so a write to a remote is judged by what the remote then holds.
// With TRAG_GIT_STUB=no-lfs it is instead a Git that finds no git-lfs, as a MinGit without it, and runs every other command.
var mode = Environment.GetEnvironmentVariable("TRAG_GIT_STUB");
if (mode == "no-lfs" && Command(args) == "lfs")
{
    Console.Error.WriteLine("git: 'lfs' is not a git command. See 'git --help'.");
    return 1;
}
if (mode == "push" && Command(args) == "push")
{
    await Console.OpenStandardInput().CopyToAsync(Stream.Null);
    return 0;
}
if (mode != "no-lfs" && Writes(args))
{
    await Console.OpenStandardInput().CopyToAsync(Stream.Null);
    return 0;
}

var start = new ProcessStartInfo("git") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
foreach (var argument in args) start.ArgumentList.Add(argument);
using var git = Process.Start(start)!;
_ = PassInputAsync(git.StandardInput);
await Task.WhenAll(git.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput()), git.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError()),
    git.WaitForExitAsync());
return git.ExitCode;

// Git may end before it read all of its input, as it does when it refuses the command.
static async Task PassInputAsync(StreamWriter input)
{
    try
    {
        await Console.OpenStandardInput().CopyToAsync(input.BaseStream);
        input.Close();
    }
    catch (IOException) { }
}

// The commands that also read are told by their arguments.
static bool Writes(string[] args)
{
    var at = 0;
    while (at < args.Length && args[at].StartsWith('-')) at += args[at] is "-c" or "-C" ? 2 : 1;
    if (at >= args.Length) return false;
    var options = args[(at + 1)..];
    var operands = options.Where(argument => !argument.StartsWith('-')).ToArray();
    return args[at] switch
    {
        "add" or "rm" or "commit" or "switch" or "checkout" or "restore" or "reset" or "clean" or "merge" or "rebase" or "cherry-pick" or "revert"
            or "update-ref" or "update-index" or "branch" or "tag" or "submodule" => true,
        "apply" => !options.Contains("--check"),
        "stash" => operands.FirstOrDefault() is not ("list" or "show"),
        "config" => !options.Any(option => option.StartsWith("--get", StringComparison.Ordinal) || option is "--list" or "-l"),
        "remote" => operands.FirstOrDefault() is "add" or "rename" or "remove" or "set-url",
        "symbolic-ref" => operands.Length == 2,
        _ => false
    };
}

// The command is the first argument past Git's own options; a command that only asks the remote, as ls-remote does,
// still reads.
static string? Command(string[] args)
{
    var at = 0;
    while (at < args.Length && args[at].StartsWith('-')) at += args[at] is "-c" or "-C" ? 2 : 1;
    return at < args.Length ? args[at] : null;
}

// With TRAG_GITSTUB_FLOOD set, a program that answers every command with that many bytes and exit 0, for the tests of a
// git.exe whose answer is larger than Trag reads of any Git output.
static async Task<int> FloodAsync(int bytes)
{
    await using var output = Console.OpenStandardOutput();
    await output.WriteAsync(new byte[bytes]);
    return 0;
}
