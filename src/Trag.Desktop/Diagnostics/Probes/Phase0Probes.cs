namespace Trag.Desktop.Diagnostics.Probes;

// The command-line probes of the phase-0 benchmarks (--vault-probe, --auth-probe, --account-probe,
// --process-probe): one minute for the whole run, one JSON line per outcome, exit code 1 on any failure.
internal static class Phase0Probes
{
    public static async Task<int> RunAsync(string[] args)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            switch (args)
            {
                case ["--vault-probe"]: return await VaultProbe.RunAsync(timeout.Token).ConfigureAwait(false);
                case ["--auth-probe"]: return await AuthProbe.RunAsync(timeout.Token).ConfigureAwait(false);
                case ["--account-probe"]: return await AccountProbe.RunAsync(timeout.Token).ConfigureAwait(false);
                case ["--process-probe"]: return await ProcessProbe.RunAsync().ConfigureAwait(false);
            }
            ProbeReport.Write("usage", "Unknown probe arguments");
            return 2;
        }
        catch (Exception error)
        {
            // Never log external output, prompts, URLs, secrets or exception messages.
            ProbeReport.Write("failure", error.GetType().Name);
            return 1;
        }
    }
}
