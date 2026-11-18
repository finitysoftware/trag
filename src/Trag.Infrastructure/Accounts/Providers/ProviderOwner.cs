namespace Trag.Infrastructure.Accounts.Providers;

// GitHub organizations carry login; GitLab groups carry id and full_path.
internal sealed class ProviderOwner
{
    public long? Id { get; set; }
    public string? Login { get; set; }
    public string? FullPath { get; set; }
}
