namespace Trag.Infrastructure.Accounts.Providers;

// One request body for both providers; null members are omitted.
internal sealed class RepositoryCreation
{
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public bool? Private { get; set; }
    public bool? AutoInit { get; set; }
    public string? Visibility { get; set; }
    public long? NamespaceId { get; set; }
    public bool? InitializeWithReadme { get; set; }
}
