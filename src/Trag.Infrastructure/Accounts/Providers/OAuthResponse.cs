namespace Trag.Infrastructure.Accounts.Providers;

internal sealed class OAuthResponse
{
    public string? DeviceCode { get; set; }
    public string? UserCode { get; set; }
    public string? VerificationUri { get; set; }
    public int ExpiresIn { get; set; }
    public int Interval { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public int RefreshTokenExpiresIn { get; set; }
    public string? Scope { get; set; }
    public string? Error { get; set; }
    public string? ErrorDescription { get; set; }
    public string? TokenType { get; set; }
}
