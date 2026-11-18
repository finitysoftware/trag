using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;

namespace Trag.Infrastructure.Accounts.Credentials;

// The named pipe Git's askpass helper connects to. One client at a time, only this user, five seconds per exchange,
// and an answer is cleared as soon as it has been written.
internal sealed class AskPassPipeServer(string pipeName, Func<CredentialRequest, byte[]?> authorize, CancellationToken lifetime)
{
    public async Task ServeAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(lifetime).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    var frame = await PipeFrames.ReadAsync(pipe, timeout.Token).ConfigureAwait(false);
                    var request = JsonSerializer.Deserialize(frame, CredentialJsonContext.Default.CredentialRequest);
                    var response = request is null ? null : authorize(request);
                    try { await PipeFrames.WriteAsync(pipe, response ?? [], timeout.Token).ConfigureAwait(false); }
                    finally { if (response is not null) CryptographicOperations.ZeroMemory(response); }
                }
                // An exchange that breaks, by a broken frame, a request or a prompt Git never writes, ends only itself.
                catch (Exception) { }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
}
