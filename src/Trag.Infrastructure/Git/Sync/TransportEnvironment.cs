using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Accounts.Credentials;

namespace Trag.Infrastructure.Git.Sync;

// What every transfer runs with: no interactive credential prompts anywhere, not even through an askpass program the
// launching terminal set (an account's own askpass is added after these), a batch SSH command with the timeouts of
// the chosen variant, only the protocols a transfer actually needs, and Git LFS reporting its progress, which it keeps to
// itself without a terminal, so a long transfer of its content is not taken for a silent one. A clone passes no configuration
// and no remote name, because it has neither yet: it is about to create them.
internal static class TransportEnvironment
{
    public static Dictionary<string, string?> Create(RemoteConfiguration? config)
    {
        var environment = new Dictionary<string, string?>
        {
            ["GIT_NO_LAZY_FETCH"] = "1", ["GCM_INTERACTIVE"] = "Never", ["GCM_GUI_PROMPT"] = "false", ["SSH_ASKPASS_REQUIRE"] = "never",
            ["GIT_ASKPASS"] = null, ["SSH_ASKPASS"] = null, ["GIT_LFS_FORCE_PROGRESS"] = "1"
        };
        if (config is null) return environment;
        environment["GIT_NO_REPLACE_OBJECTS"] = "1";
        environment["GIT_SSH_VARIANT"] = config.SshVariant;
        environment["GIT_SSH_COMMAND"] = config.SshCommand
            + (config.SshVariant == "ssh" ? " -oBatchMode=yes -oConnectTimeout=15 -oServerAliveInterval=15 -oServerAliveCountMax=2" : " -batch");
        return environment;
    }

    public static string[] Setup(string? remote) =>
    [
        "-c", "protocol.allow=never", "-c", "protocol.file.allow=always", "-c", "protocol.ssh.allow=always",
        "-c", "protocol.https.allow=always", "-c", "protocol.http.allow=always", "-c", "protocol.git.allow=always",
        .. remote is null ? Array.Empty<string>() : ["-c", "fetch.writeCommitGraph=false", "-c", "push.autoSetupRemote=false"],
        "-c", "submodule.recurse=false",
        .. remote is null ? Array.Empty<string>() : ["-c", "remote." + remote + ".mirror=false"]
    ];

    // A submodule's address comes from the repository's own .gitmodules, not from the user, so a local repository is cloned
    // or fetched for it only as Git itself allows since CVE-2022-39253: never, unless the user's own configuration says
    // otherwise. The other protocols stay as the setup has them.
    public static string[] ForSubmodules(string[] setup, string? fileProtocol) => [.. setup, "-c", "protocol.file.allow=" + (fileProtocol ?? "user")];

    // An account's credentials reach Git only through the isolated askpass helper, and only the published application
    // has one. This is asked before a secret is read from the vault, so a missing helper costs nothing.
    public static void RequireAskPass(string? executable)
    {
        if (string.IsNullOrEmpty(executable)) throw new AccountException(SyncOutcomeText.AskpassMissing);
    }

    // The helper's variables join the environment and its configuration precedes the command: no other credential
    // helper, no prompt, and the endpoint the broker answers for.
    public static string[] Authenticated(Dictionary<string, string?> environment, string[] setup, CredentialBroker broker, string askPassExecutable)
    {
        foreach (var item in broker.CreateEnvironment(askPassExecutable)) environment[item.Key] = item.Value;
        return [.. setup, .. CredentialBroker.IsolatedHelperArguments, .. broker.EndpointArguments];
    }
}
