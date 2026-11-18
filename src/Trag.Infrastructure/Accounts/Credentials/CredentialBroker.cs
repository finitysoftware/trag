using System.Security.Cryptography;
using Trag.Domain.Accounts;

namespace Trag.Infrastructure.Accounts.Credentials;

// One broker is bound to an explicit account, HTTPS origin and live operation, and answers for as long as the operation
// runs: a recursive submodule update asks again minutes in, for each repository it clones. The pipe name and operation ID
// are routing metadata; OS permissions protect it.
public sealed class CredentialBroker : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime;
    private readonly byte[] secret;
    private readonly Task server;
    private readonly CredentialAuthorizer authorizer;
    private readonly CredentialRedactor redactor;
    private readonly GitCredentialArguments arguments;
    private int disposed;

    public string PipeName { get; } = AskPassClient.PipePrefix + Guid.NewGuid().ToString("N");
    public string OperationId { get; } = Guid.NewGuid().ToString("N");

    public CredentialBroker(Uri origin, string userName, ReadOnlySpan<byte> secret, CancellationToken operationToken)
    {
        if (!AccountEndpoints.IsClean(origin)) throw new ArgumentException("A credential origin must be a clean HTTPS URL.", nameof(origin));
        if (!AccountName.IsValid(userName)) throw new ArgumentException("Invalid user name.", nameof(userName));
        if (!TokenValidation.IsValid(secret)) throw new ArgumentException("Invalid credential value.", nameof(secret));
        this.secret = secret.ToArray();
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(operationToken);
        authorizer = new(origin, userName, this.secret, OperationId, lifetime.Token);
        redactor = new(userName, this.secret);
        arguments = new(origin, userName, PipeName, OperationId);
        server = Task.Run(new AskPassPipeServer(PipeName, Authorize, lifetime.Token).ServeAsync);
    }

    public Dictionary<string, string?> CreateEnvironment(string executable) => arguments.CreateEnvironment(executable);

    public string[] EndpointArguments => arguments.EndpointArguments;

    public static string[] IsolatedHelperArguments => GitCredentialArguments.IsolatedHelperArguments;

    public string Redact(string text) => redactor.Redact(text);

    public byte[]? Authorize(CredentialRequest request)
    {
        return Volatile.Read(ref disposed) != 0 ? null : authorizer.Authorize(request);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await lifetime.CancelAsync().ConfigureAwait(false);
        try { await server.ConfigureAwait(false); }
        finally { CryptographicOperations.ZeroMemory(secret); lifetime.Dispose(); }
    }
}
