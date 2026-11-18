using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Benchmarks;

internal static class RepositoryWriteUiSmoke
{
    public static async Task RunAsync(string executable, string directory, CancellationToken ct)
    {
        var output = Directory.CreateDirectory(Path.GetFullPath(directory)).FullName;
        var fixture = Directory.CreateDirectory(Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N"))).FullName;
        var hooks = Path.Combine(fixture, ".git", "fixture-hooks");
        var runner = new GitProcessRunner();
        async Task<string> Git(params string[] args)
        {
            var result = await runner.RunAsync(fixture, args, ct);
            if (result.ExitCode != 0) throw new InvalidOperationException(Encoding.UTF8.GetString(result.StandardError));
            return Encoding.UTF8.GetString(result.StandardOutput);
        }
        await Git("init", "--initial-branch=main");
        Directory.CreateDirectory(hooks);
        await Git("config", "user.name", "Trag Fixture");
        await Git("config", "user.email", "fixture@example.invalid");
        await Git("config", "commit.gpgsign", "false");
        await Git("config", "core.hooksPath", hooks);
        await Git("config", "core.autocrlf", "false");
        var partial = Path.Combine(fixture, "a partial č.txt");
        await File.WriteAllTextAsync(partial, "base\n", ct);
        await File.WriteAllTextAsync(Path.Combine(fixture, "b.txt"), "second\n", ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var token = timeout.Token;
        var transcript = new List<string>();
        await using var ui = new ProbeSession(executable, Path.Combine(output, "settings.json"), transcript, token);
        Task Message(string message) => ui.Expect("message " + Convert.ToBase64String(Encoding.UTF8.GetBytes(message)), "TRAG_MESSAGE_OK");
        Task Screenshot(string name) => ui.Shot(Path.Combine(output, name + ".png"), 250);
        await ui.Expect("theme light", "TRAG_THEME_OK");
        await ui.Expect("open " + fixture, "TRAG_OPEN 1 0 2 0");
        await ui.Expect("select-many unstaged 0 1", "TRAG_SELECTION_COUNT 2");
        await ui.Expect("stage", "TRAG_WRITE 1 2 0 0");
        await ui.Expect("select-many staged 0 1", "TRAG_SELECTION_COUNT 2");
        await ui.Expect("unstage", "TRAG_WRITE 1 0 2 0");
        await ui.Expect("select-many unstaged 0", "TRAG_SELECTION_COUNT 1");
        await ui.Expect("stage", "TRAG_WRITE 1 1 1 0");
        await Message("First commit č\n\nOnly the selected staged file.");
        await Screenshot("first-commit-light");
        await ui.Expect("commit", "TRAG_WRITE 1 0 1 0");
        var first = (await Git("rev-parse", "HEAD")).Trim();
        await ui.Expect("write-state", "TRAG_WRITE_STATE 0 0 0 " + first);
        if (await Git("ls-tree", "-z", "--name-only", "HEAD") != "a partial č.txt\0")
            throw new InvalidOperationException("The first commit included an unselected file.");
        await File.WriteAllTextAsync(partial, "base\nstaged\n", token);
        await Git("add", "--", "a partial č.txt");
        await File.AppendAllTextAsync(partial, "unstaged\n", token);
        await ui.Expect("refresh", "TRAG_REFRESH 1 1 2 0");
        var hook = Path.Combine(hooks, "pre-commit");
        await File.WriteAllTextAsync(hook, "#!/bin/sh\necho 'Fixture hook rejected the commit' >&2\nexit 1\n", new UTF8Encoding(false), token);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        await Message("Preserve the unstaged line");
        await ui.Expect("refresh", "TRAG_REFRESH 1 1 2 0");
        await ui.Expect("commit", "TRAG_WRITE 1 1 2 1");
        await ui.Expect("write-state", "TRAG_WRITE_STATE 1 0 0 ");
        if ((await Git("rev-parse", "HEAD")).Trim() != first) throw new InvalidOperationException("Failed hook changed HEAD.");
        await ui.Expect("theme dark", "TRAG_THEME_OK");
        await Screenshot("hook-error-dark");
        File.Delete(hook); // This fixture owns this hook.
        await ui.Expect("commit", "TRAG_WRITE 1 0 2 0");
        var second = (await Git("rev-parse", "HEAD")).Trim();
        await ui.Expect("write-state", "TRAG_WRITE_STATE 0 0 0 " + second);
        if ((await Git("rev-parse", "HEAD^")).Trim() != first || await Git("show", "HEAD:a partial č.txt") != "base\nstaged\n"
            || await File.ReadAllTextAsync(partial, token) != "base\nstaged\nunstaged\n") throw new InvalidOperationException("Partial staging was not preserved.");
        await ui.Expect("size 880 580", "TRAG_SIZE_OK");
        await Screenshot("committed-compact-dark");
        await ui.Expect("close", "TRAG_CLOSE 0 0 0 0");
        if (await ui.QuitAsync() != 0) throw new InvalidOperationException("UI exited unsuccessfully.");
        await File.WriteAllTextAsync(Path.Combine(output, "write-smoke.json"), JsonSerializer.Serialize(new
        {
            TimestampUtc = DateTimeOffset.UtcNow, ExecutableSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executable, token))),
            Fixture = fixture, FirstCommit = first, SecondCommit = second, Transcript = transcript, StandardError = await ui.Errors,
            PartialStagingPreserved = true, FailedHookMessagePreserved = true, Result = "passed"
        }, new JsonSerializerOptions { WriteIndented = true }), token);
        Console.WriteLine("Native write UI smoke passed: group stage/unstage, first and following commit, hook failure and partial staging.");
    }
}
