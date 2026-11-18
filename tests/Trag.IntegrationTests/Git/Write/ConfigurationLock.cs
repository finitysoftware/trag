using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// Git rewrites the whole configuration through config.lock, after the refs of a write. A long configuration keeps that
// lock there long enough to stop Git inside it: the write is canceled as soon as the lock appears.
internal static class ConfigurationLock
{
    public static Task LengthenAsync(GitFixture f) => File.AppendAllTextAsync(Path.Combine(f.Root, ".git", "config"),
        string.Concat(Enumerable.Range(0, 150_000).Select(n => "[trag \"s" + n + "\"]\n\tk = v\n")), f.Token);

    public static async Task<GitWriteResult> CancelWhileHeldAsync(GitFixture f, GitWriteRequest request)
    {
        using var cancellation = new CancellationTokenSource();
        var held = Path.Combine(f.Root, ".git", "config.lock");

        var pending = new GitWriteRouter(f.Runner).ExecuteAsync(request, cancellation.Token);
        var seen = await Task.Run(() =>
        {
            while (!pending.IsCompleted) if (File.Exists(held)) return true;
            return false;
        }, f.Token);
        await cancellation.CancelAsync();
        var result = await pending;

        Assert.True(seen, "Git never held config.lock: " + result.Outcome + " " + result.Message);
        return result;
    }
}
