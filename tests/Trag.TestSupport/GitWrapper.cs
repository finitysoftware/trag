using Trag.Infrastructure.Git.Processes;

namespace Trag.TestSupport;

// A Git program in the real one's place that sets environment variables and puts arguments before the caller's.
public static class GitWrapper
{
    public static Task<string> WriteAsync(string folder, string name, IEnumerable<(string Name, string Value)> environment, params string[] arguments)
    {
        var git = new GitProcessRunner().Program;
        var variables = environment.ToArray();
        return TestProgram.WriteAsync(folder, name,
            string.Concat(variables.Select(variable => $"@set {variable.Name}={variable.Value}\r\n")) + "@\"" + git + "\" " + string.Join(' ', arguments) + " %*\r\n",
            string.Concat(variables.Select(variable => $"export {variable.Name}={TestProgram.Quote(variable.Value)}\n"))
                + "exec " + TestProgram.Quote(git) + string.Concat(arguments.Select(argument => " " + TestProgram.Quote(argument))) + " \"$@\"\n");
    }
}
