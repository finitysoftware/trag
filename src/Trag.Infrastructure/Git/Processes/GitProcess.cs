using System.Diagnostics;

namespace Trag.Infrastructure.Git.Processes;

// One owner for the started process: its streams and its termination. Draining a stream stops the process on any
// failure, so a limit that was exceeded or a read that broke never leaves Git blocked on a full pipe.
internal sealed class GitProcess : IAsyncDisposable
{
    // Git's own output is in the pipes when it exits. A job it started that still holds them, as a hook's background job
    // does, is not waited for: the streams end this long after Git, time enough to read what Git left in them.
    private static readonly TimeSpan StreamsAfterExit = TimeSpan.FromSeconds(2);

    private readonly Process process;
    private readonly Action<int>? exited;
    private readonly CancellationTokenSource afterExit = new();
    private CancellationTokenRegistration registration;
    private WindowsProcessJob? job;

    private GitProcess(ProcessStartInfo start, Action<int>? exited) => (process, this.exited) = (new() { StartInfo = start }, exited);

    // A started Git: its input closed unless the caller writes it, and stopped when the token is cancelled. Disposing stops
    // it and waits for it, then reports its id to whoever counts the processes that ran.
    public static GitProcess Launch(ProcessStartInfo start, bool writesInput, Action<int>? exited, CancellationToken ct)
    {
        var started = new GitProcess(start, exited);
        try
        {
            ct.ThrowIfCancellationRequested();
            started.process.Start();
            if (OperatingSystem.IsWindows()) started.job = new(started.process);
        }
        catch
        {
            started.process.Dispose();
            throw;
        }
        // A GUI process can inherit a live pipe; never pass that input to Git.
        if (!writesInput) started.CloseStandardInput();
        started.registration = ct.Register(started.Terminate);
        return started;
    }

    public int ExitCode => process.ExitCode;
    // Trag stopped Git before it ended by itself.
    public bool Stopped { get; private set; }
    public Stream StandardInput => process.StandardInput.BaseStream;
    public Stream StandardOutput => process.StandardOutput.BaseStream;
    public Stream StandardError => process.StandardError.BaseStream;

    public void CloseStandardInput() => process.StandardInput.Close();

    public async Task WaitForExitAsync(CancellationToken ct)
    {
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        afterExit.CancelAfter(StreamsAfterExit);
    }

    public void Terminate()
    {
        try
        {
            if (process.HasExited) return;
            // Set first: Git can be gone, and its exit awaited, while the stop still goes through its children. On Windows
            // the job reaches every process Git started; elsewhere the tree does.
            Stopped = true;
            if (job is not null) job.Terminate();
            else process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
    }

    // Everything the stream carries, bounded twice: by the caller's prefix limit, which is an answer (the process is
    // stopped and the prefix is returned as truncated output), and by the runner's output limit, which is a failure. A
    // stream still held after Git exited ends with what Git wrote to it. A cancellation does not end it: it stops Git, and
    // the stream ends with Git.
    public async Task<(byte[] Bytes, bool Truncated)> ReadAllAsync(Stream stream, int outputLimit, int? prefixLimit, GitTransferProgress? progress, Action? heard = null)
    {
        using var buffer = new MemoryStream();
        try
        {
            var chunk = new byte[16 * 1024];
            int length;
            while ((length = await stream.ReadAsync(chunk, afterExit.Token).ConfigureAwait(false)) != 0)
            {
                heard?.Invoke();
                progress?.Feed(chunk.AsSpan(0, length));
                if (prefixLimit is { } limit && buffer.Length + length > limit)
                {
                    buffer.Write(chunk, 0, Math.Max(0, limit - (int)buffer.Length));
                    Terminate();
                    return (buffer.ToArray(), true);
                }
                if (buffer.Length + length > outputLimit) throw new GitOutputLimitException();
                buffer.Write(chunk, 0, length);
            }
        }
        catch (OperationCanceledException) { }
        catch { Terminate(); throw; }
        return (buffer.ToArray(), false);
    }

    // stdout for a caller that reads it as it arrives: it ends with Git, as ReadAllAsync's streams do, once the caller
    // waits for Git's exit.
    public Stream StandardOutputUntilExit => new EndingStream(StandardOutput, afterExit.Token);

    // A stream nobody reads is still drained, so the process never blocks on it, and still refused past the limit.
    public async Task DiscardAsync(Stream stream, int outputLimit, CancellationToken ct)
    {
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(ct, afterExit.Token);
        try
        {
            var bytes = new byte[4096];
            var total = 0;
            int count;
            while ((count = await stream.ReadAsync(bytes, reading.Token).ConfigureAwait(false)) != 0)
                if ((total += count) > outputLimit) throw new GitOutputLimitException();
        }
        catch (OperationCanceledException) when (afterExit.IsCancellationRequested) { }
        catch { Terminate(); throw; }
    }

    // The input is written whole and the pipe closed either way, so Git sees the end of it.
    public async Task WriteInputAsync(byte[] input, CancellationToken ct)
    {
        try { await StandardInput.WriteAsync(input, ct).ConfigureAwait(false); }
        catch { Terminate(); throw; }
        finally { CloseStandardInput(); }
    }

    public async ValueTask DisposeAsync()
    {
        Terminate();
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        exited?.Invoke(process.Id);
        registration.Dispose();
        afterExit.Dispose();
        job?.Dispose();
        process.Dispose();
    }

    // What the reader asks for once the end comes is the end of the stream; its own cancellation stays its own.
    private sealed class EndingStream(Stream inner, CancellationToken end) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            using var reading = CancellationTokenSource.CreateLinkedTokenSource(ct, end);
            try { return await inner.ReadAsync(buffer, reading.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (end.IsCancellationRequested && !ct.IsCancellationRequested) { return 0; }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
