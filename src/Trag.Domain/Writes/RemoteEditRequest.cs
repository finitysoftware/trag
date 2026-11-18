using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// A remote of this repository renamed, given another address, or both; Url null keeps its address.
public sealed record RemoteEditRequest(RepositorySnapshot Expected, string Name, string NewName, string? Url) : GitWriteRequest(Expected);
