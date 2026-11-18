namespace Trag.Domain.Status;

// Remote addresses have already been redacted by the infrastructure adapter.
public sealed record RepositoryRemote(string Name, string FetchAddress, string PushAddress);
