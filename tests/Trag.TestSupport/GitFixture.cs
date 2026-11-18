using System.Text;
using Trag.Domain.Status;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;

namespace Trag.TestSupport;

// A throwaway repository under the temp folder driven by the real git CLI: the one fixture of the integration tests
// and of the unit tests that need real Git. A failing git command throws with its stderr; every member without a root
// works in Root, and GitAtAsync / RunAtAsync name the folder for a bare remote or a second clone in the container.
public sealed class GitFixture : IDisposable
{
    private const string Prefix = "trag-fixture-";
    private readonly CancellationTokenSource timeout = new(TimeSpan.FromSeconds(120));

    public string Container { get; } = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));
    public string Root => Path.Combine(Container, "working copy č");
    public GitProcessRunner Runner { get; } = new();
    // Generous on purpose: the suite runs classes in parallel and each scenario spawns dozens of git processes; a hung test still fails.
    public CancellationToken Token => timeout.Token;

    public static async Task<GitFixture> CreateAsync(string initialBranch = "fixture", TimeSpan? scenarioTimeout = null, string? objectFormat = null)
    {
        var fixture = new GitFixture();
        if (scenarioTimeout is { } duration) fixture.timeout.CancelAfter(duration);
        Directory.CreateDirectory(fixture.Root);
        try
        {
            await fixture.GitAsync(["init", "--initial-branch=" + initialBranch, .. objectFormat is null ? [] : new[] { "--object-format=" + objectFormat }]);
            await fixture.GitAsync("config", "user.name", "Trag Fixture");
            await fixture.GitAsync("config", "user.email", "fixture@example.invalid");
            await fixture.GitAsync("config", "commit.gpgsign", "false");
            await fixture.GitAsync("config", "core.hooksPath", Path.Combine(fixture.Container, "empty-hooks"));
            return fixture;
        }
        catch { fixture.Dispose(); throw; }
    }

    public Task<GitResult> RunAsync(params string[] arguments) => RunAtAsync(Root, arguments);
    public Task<GitResult> RunAtAsync(string root, params string[] arguments) => Runner.RunAsync(root, arguments, Token);

    public Task<string> GitAsync(params string[] arguments) => GitAtAsync(Root, arguments);

    public async Task<string> GitAtAsync(string root, params string[] arguments)
    {
        var result = await RunAtAsync(root, arguments).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new InvalidOperationException("git " + string.Join(' ', arguments) + " failed: " + Encoding.UTF8.GetString(result.StandardError));
        return Encoding.UTF8.GetString(result.StandardOutput).Trim();
    }

    public async Task WriteAsync(string relativePath, string content)
    {
        var full = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content, new UTF8Encoding(false), Token).ConfigureAwait(false);
    }

    // Stages the given paths, or everything, commits and answers the new head.
    public async Task<string> CommitAsync(string message, params string[] paths)
    {
        if (paths.Length == 0) await GitAsync("add", "--all").ConfigureAwait(false);
        else await GitAsync(["add", "--", .. paths]).ConfigureAwait(false);
        await GitAsync("commit", "--allow-empty", "-m", message).ConfigureAwait(false);
        return await GitAsync("rev-parse", "HEAD").ConfigureAwait(false);
    }

    public Task<RepositorySnapshot> ReadAsync(string? path = null, CancellationToken? cancellationToken = null) =>
        new CliRepositoryReader(Runner).ReadAsync(path ?? Root, cancellationToken ?? Token);

    public void Dispose()
    {
        timeout.Cancel();
        timeout.Dispose();
        FixtureDirectory.Delete(Container, Prefix);
    }
}
