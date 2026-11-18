namespace Trag.Infrastructure.Accounts.Credentials;

// How Git is told to ask Trag and nobody else: the askpass executable and the pipe to reach it, every configured
// credential helper turned off, no extra headers, no redirects, and TLS verification on for this destination.
internal sealed class GitCredentialArguments(Uri origin, string userName, string pipeName, string operationId)
{
    public Dictionary<string, string?> CreateEnvironment(string executable) => new()
    {
        // This is an executable path, not a shell command. Git quotes it itself.
        ["GIT_ASKPASS"] = Path.GetFullPath(executable),
        [AskPassClient.MarkerVariable] = "1",
        [AskPassClient.PipeVariable] = pipeName,
        [AskPassClient.OperationVariable] = operationId,
        ["GIT_TERMINAL_PROMPT"] = "0",
        // Git skips certificate verification whenever this variable exists, even as "false", so it is removed.
        ["GIT_SSL_NO_VERIFY"] = null,
        ["GIT_CURL_VERBOSE"] = null,
        ["LC_ALL"] = "C"
    };

    public string[] EndpointArguments => ["-c", "credential." + origin.AbsoluteUri + ".helper=",
        "-c", "credential." + origin.AbsoluteUri + ".username=" + userName,
        "-c", "http." + origin.AbsoluteUri + ".extraHeader=", "-c", "http." + origin.AbsoluteUri + ".sslVerify=true"];

    public static string[] IsolatedHelperArguments => ["-c", "credential.helper=", "-c", "credential.interactive=true", "-c", "credential.useHttpPath=true",
        "-c", "http.extraHeader=", "-c", "http.sslVerify=true", "-c", "http.followRedirects=false"];
}
