namespace Trag.TestSupport;

// A repository with one submodule, "sub", cloned from a second repository beside it: "Base" adds a.txt, "Add sub" adds
// the submodule. The submodule's address is a file:// one, or, served, a loopback HTTP one, the transport Git allows a
// submodule a clone of the repository makes. Both repositories commit as the fixture identity and never sign.
public static class SubmoduleFixture
{
    public static Task<GitFixture> CreateAsync() => CreateAsync((_, origin) => Task.FromResult(new Uri(origin).AbsoluteUri));

    public static async Task<(GitFixture Fixture, LoopbackGitServer Server)> CreateServedAsync()
    {
        LoopbackGitServer? server = null;
        try
        {
            var f = await CreateAsync(async (fixture, origin) =>
            {
                server = new(fixture.Container);
                await fixture.GitAtAsync(origin, "update-server-info");
                return server.Url("sub-origin/.git");
            });
            return (f, server!);
        }
        catch { server?.Dispose(); throw; }
    }

    private static async Task<GitFixture> CreateAsync(Func<GitFixture, string, Task<string>> address)
    {
        var f = await GitFixture.CreateAsync("main");
        try
        {
            var origin = Origin(f);
            Directory.CreateDirectory(origin);
            await f.GitAtAsync(origin, "init", "--initial-branch=main");
            await IdentifyAsync(f, origin);
            await f.GitAtAsync(origin, "commit", "--allow-empty", "-m", "Sub one");
            await f.WriteAsync("a.txt", "a\n");
            await f.CommitAsync("Base", "a.txt");
            await f.GitAsync("-c", "protocol.file.allow=always", "submodule", "add", "--", await address(f, origin), "sub");
            await IdentifyAsync(f, Path.Combine(f.Root, "sub"));
            await f.GitAsync("commit", "-m", "Add sub");
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    public static string Origin(GitFixture f) => Path.Combine(f.Container, "sub-origin");

    // A repository inside the fixture commits as the fixture does.
    public static async Task IdentifyAsync(GitFixture f, string repository)
    {
        await f.GitAtAsync(repository, "config", "user.name", "Trag Fixture");
        await f.GitAtAsync(repository, "config", "user.email", "fixture@example.invalid");
        await f.GitAtAsync(repository, "config", "commit.gpgsign", "false");
    }
}
