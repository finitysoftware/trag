namespace Trag.Infrastructure.Accounts.Providers;

// Internal secret-bearing types intentionally have no generated record ToString.
internal sealed class TokenData
{
    public string AccessToken { get; set; } = "";
    public string? RefreshToken { get; set; }
    public string? ClientId { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RefreshExpiresAt { get; set; }
    public bool RequiresIdentityVerification { get; set; }
}
