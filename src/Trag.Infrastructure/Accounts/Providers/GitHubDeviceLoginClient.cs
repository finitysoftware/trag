using Trag.Domain.Accounts;

namespace Trag.Infrastructure.Accounts.Providers;

// GitHub's device flow in its four states: a code is requested, the person is waiting to approve it, GitHub may ask
// for a slower pace, and an approved code is verified as a token. Polling honours the interval GitHub asks for and the
// deadline it set, and the user's cancellation ends any wait. Nothing here has a client secret to lose.
internal sealed class GitHubDeviceLoginClient(GitHubTokenClient tokens, TimeProvider clock)
{
    // Trag requests the full client permission set up front, so one sign-in covers repositories,
    // workflows, organizations, hooks, keys, gists, notifications and profile data. GitHub shows every item on its approval page.
    public const string Scopes = "repo workflow admin:org admin:org_hook admin:repo_hook admin:public_key admin:gpg_key delete_repo gist notifications user offline_access";
    // The public Client ID of Trag's OAuth application on GitHub, with Device Flow enabled. A renewal uses the one stored
    // with its token instead, so a token issued under an earlier application renews there.
    private const string ClientId = "Ov23liBO0BGpinG9cmk4";
    private const string CodeUrl = "https://github.com/login/device/code";
    private const string DeviceGrant = "urn:ietf:params:oauth:grant-type:device_code";

    public async Task<TokenData> DeviceLoginAsync(IProgress<DeviceLoginProgress> progress, CancellationToken ct)
    {
        progress.Report(new(DeviceLoginState.RequestingCode));
        var response = await tokens.OAuthAsync(CodeUrl, new() { ["client_id"] = ClientId, ["scope"] = Scopes }, ct).ConfigureAwait(false);
        var code = DeviceCode.Parse(response, clock.GetUtcNow());
        progress.Report(new(DeviceLoginState.Waiting, code.UserCode, code.VerificationUri));
        while (!code.Expired(clock.GetUtcNow()))
        {
            await Task.Delay(code.DelayUntilNextPoll(clock.GetUtcNow()), clock, ct).ConfigureAwait(false);
            if (code.Expired(clock.GetUtcNow())) break;
            var poll = await tokens.OAuthAsync(GitHubTokenClient.TokenUrl, new()
                { ["client_id"] = ClientId, ["device_code"] = code.Value, ["grant_type"] = DeviceGrant }, ct).ConfigureAwait(false);
            if (poll.Error == "authorization_pending") continue;
            if (poll.Error == "slow_down")
            {
                code.SlowDown(poll.Interval);
                progress.Report(new(DeviceLoginState.SlowedDown, code.UserCode, code.VerificationUri));
                continue;
            }
            if (poll.Error is not null) throw OAuthErrorMessages.Explain(poll);
            progress.Report(new(DeviceLoginState.Verifying));
            return tokens.Tokens(poll, ClientId);
        }
        throw OAuthErrorMessages.Explain("expired_token");
    }
}
