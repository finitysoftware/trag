namespace Trag.Domain.Accounts;

public sealed record DeviceLoginProgress(DeviceLoginState State, string UserCode = "", Uri? VerificationUri = null);
