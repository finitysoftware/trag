namespace Trag.Domain.Catalog;

public sealed record RepositoryProbe(RepositoryLinkStatus Status, string? Branch = null, string? RemoteUrl = null, string? Root = null);
