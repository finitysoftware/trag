using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;

namespace Trag.Infrastructure.Accounts.Credentials;

// The askpass protocol's Trag side: Git starts this exe with the marker, the pipe and the operation GitCredentialArguments
// set, the prompt as the one argument, and the reply comes from the broker of that operation over the pipe.
public static class AskPassClient
{
    internal const string MarkerVariable = "TRAG_ASKPASS";
    internal const string PipeVariable = "TRAG_ASKPASS_PIPE";
    internal const string OperationVariable = "TRAG_ASKPASS_OPERATION";
    internal const string PipePrefix = "trag-";

    // A launch Git started as its askpass helper; it runs before anything that could touch the UI.
    public static bool IsAskPassLaunch(Func<string, string?> environment) => environment(MarkerVariable) is not null;

    public static bool IsValidContext(string? marker, string? pipe, string[] args) =>
        marker == "1" && pipe is not null && pipe.StartsWith(PipePrefix, StringComparison.Ordinal)
        && pipe.Length <= 80 && pipe.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') && args.Length == 1;

    // Git's call: a context Trag did not set up answers 2 without opening a pipe; otherwise the broker's reply, within
    // five seconds.
    public static int RunFromEnvironment(Func<string, string?> environment, string[] args, Stream output)
    {
        var (marker, pipe, operation) = (environment(MarkerVariable), environment(PipeVariable), environment(OperationVariable));
        if (!IsValidContext(marker, pipe, args) || !Guid.TryParseExact(operation, "N", out _)) return 2;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return RunAsync(pipe!, operation!, args[0], output, timeout.Token).GetAwaiter().GetResult();
    }

    public static async Task<int> RunAsync(string pipeName, string operationId, string prompt, Stream output, CancellationToken ct)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(ct).ConfigureAwait(false);
            var request = JsonSerializer.SerializeToUtf8Bytes(new CredentialRequest(operationId, prompt), CredentialJsonContext.Default.CredentialRequest);
            await PipeFrames.WriteAsync(pipe, request, ct).ConfigureAwait(false);
            var value = await PipeFrames.ReadAsync(pipe, ct).ConfigureAwait(false);
            try
            {
                if (value.Length == 0) return 2;
                await output.WriteAsync(value, ct).ConfigureAwait(false);
                await output.WriteAsync("\n"u8.ToArray(), ct).ConfigureAwait(false);
                await output.FlushAsync(ct).ConfigureAwait(false);
                return 0;
            }
            finally { CryptographicOperations.ZeroMemory(value); }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException) { return 2; }
    }
}
