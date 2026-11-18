using System.Diagnostics;
using System.Text;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Benchmarks;

internal static class FixtureGenerator
{
    public static async Task GenerateAsync(string root, int commits, int files, CancellationToken ct)
    {
        if (commits < 1 || files < 1) throw new ArgumentOutOfRangeException(nameof(commits));
        root = Path.GetFullPath(root);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new InvalidOperationException("Fixture destination must be new or empty.");
        Directory.CreateDirectory(root);
        var runner = new GitProcessRunner();
        await RunGit("init", "--initial-branch=fixture");
        await File.WriteAllTextAsync(Path.Combine(root, ".git", "trag-fixture"), "Generated disposable fixture", ct);
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("fast-import"); start.ArgumentList.Add("--quiet");
        using var process = Process.Start(start) ?? throw new InvalidOperationException();
        using var registration = ct.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await using (var writer = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false), 64 * 1024, leaveOpen: false) { NewLine = "\n" })
        {
            for (var commit = 0; commit < commits; commit++)
            {
                ct.ThrowIfCancellationRequested();
                await writer.WriteAsync($"commit refs/heads/fixture\ncommitter Fixture <fixture@example.invalid> {1_700_000_000L + commit} +0000\n".AsMemory(), ct);
                await Data($"Synthetic commit {commit:D6}\n");
                if (commit == 0)
                {
                    for (var file = 0; file < files; file++)
                    {
                        await writer.WriteAsync($"M 100644 inline data/f{file:D5}.txt\n".AsMemory(), ct);
                        await Data("original\n");
                    }
                    await writer.WriteAsync("M 100644 inline blame.txt\n".AsMemory(), ct);
                    await Data("stable blame line\n");
                }
                await writer.WriteAsync("M 100644 inline history.txt\n".AsMemory(), ct);
                await Data($"revision {commit}\n");
                await writer.WriteAsync("\n".AsMemory(), ct);
            }
            await writer.WriteAsync("done\n".AsMemory(), ct);
            async Task Data(string value) => await writer.WriteAsync($"data {Encoding.UTF8.GetByteCount(value)}\n{value}\n".AsMemory(), ct);
        }
        await process.WaitForExitAsync(ct);
        _ = await stdout;
        var errors = await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException("Fixture import failed: " + errors);
        // Confined to this newly generated fixture, never the source repository.
        await RunGit("reset", "--hard", "HEAD");
        for (var file = 0; file < files; file++)
            await File.WriteAllTextAsync(Path.Combine(root, "data", $"f{file:D5}.txt"), "original\nchanged\n", ct);
        Console.WriteLine($"Generated {commits} commits and {files} changed files in {root}");

        async Task RunGit(params string[] arguments)
        {
            var result = await runner.RunAsync(root, arguments, ct);
            if (result.ExitCode != 0) throw new InvalidOperationException("Fixture Git operation failed.");
        }
    }
}
