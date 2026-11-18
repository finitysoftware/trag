namespace Trag.Infrastructure.Accounts.Providers;

// One shape for both providers; unknown JSON members are ignored.
internal sealed class ProviderRepository
{
    public string? FullName { get; set; }
    public string? PathWithNamespace { get; set; }
    public string? CloneUrl { get; set; }
    public string? HttpUrlToRepo { get; set; }
    public string? HtmlUrl { get; set; }
    public string? WebUrl { get; set; }
    public string? DefaultBranch { get; set; }
    public bool? Private { get; set; }
    public string? Visibility { get; set; }
    public DateTimeOffset? PushedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
}
