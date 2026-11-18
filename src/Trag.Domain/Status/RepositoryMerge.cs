namespace Trag.Domain.Status;

public sealed record RepositoryMerge(string? IncomingHead, string? OriginalHead, string Message, string Fingerprint, string Limitation);
