using Trag.TestSupport;

namespace Trag.IntegrationTests;

// A Git that never answers: a program in the real one's place that notes each start and then waits ten minutes
// without a word, whatever it was asked. Only stopping it, with everything it started, ends it sooner.
internal sealed class HungGit
{
    private readonly string started;

    private HungGit(string program, string started) => (Program, this.started) = (program, started);

    public string Program { get; }

    public static async Task<HungGit> CreateAsync(string folder, CancellationToken ct)
    {
        var started = Path.Combine(folder, "hung-git.log");
        var program = await TestProgram.WriteAsync(folder, "hung-git", "@echo started>>\"" + started + "\"\r\n@ping -n 601 127.0.0.1 >nul\r\n",
            "echo started >> " + TestProgram.Quote(started) + "\nsleep 600\n", ct);
        return new(program, started);
    }

    // Returns once a run reached it.
    public async Task StartedAsync(CancellationToken ct)
    {
        while (!File.Exists(started)) await Task.Delay(20, ct);
    }
}
