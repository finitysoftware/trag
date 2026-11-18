using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Ports.Git;
using Trag.Application.Ports.Storage;
using Trag.Domain.Setup;

namespace Trag.Application.Setup;

// The Git Trag runs: looked for at the start and whenever asked again, the user's chosen git.exe first, and the start
// goes on once there is a Git to run. A chosen git.exe is remembered only when it runs as a new enough Git; one that
// fails while a Git runs leaves the running one shown, and says why to whoever chose it. A chosen Git that no longer runs
// gives way to one found along the PATH and is forgotten, but is kept, with what is wrong with it said, while there is no
// other. Given up, a choice goes only once Trag finds another Git. A choice that cannot be read is as none, and one that
// cannot be written still runs for as long as Trag does. Looks and choices take turns, so each sees what the one before
// found and remembered.
public sealed partial class GitSetup(IGitInstallations installations, IGitChoiceStore? choice) : ObservableObject
{
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim turn = new(1, 1);

    [ObservableProperty] public partial GitFinding Finding { get; private set; } = new(GitProblem.None);

    // True once there is a Git to run; false when the application went before there was one.
    public async Task<bool> StartAsync(CancellationToken ct)
    {
        try
        {
            await CheckAsync(ct);
            await ready.Task.WaitAsync(ct);
            return true;
        }
        catch (OperationCanceledException) { return false; }
    }

    public async Task CheckAsync(CancellationToken ct)
    {
        await turn.WaitAsync(ct);
        try { Show(await LookAsync(installations, ct)); }
        finally { turn.Release(); }
    }

    public async Task<GitFinding> ChooseAsync(string path, CancellationToken ct)
    {
        await turn.WaitAsync(ct);
        try
        {
            var finding = await installations.FindAsync(path, ct);
            if (finding.Runs) await RememberAsync(path, ct);
            if (finding.Runs || !ready.Task.IsCompleted) Show(finding);
            return finding;
        }
        finally { turn.Release(); }
    }

    // What the running Git brings; null while there is none to run.
    public Task<GitFeatures?> DescribeAsync(CancellationToken ct) =>
        Finding.Running is { } git ? installations.DescribeAsync(git, ct) : Task.FromResult<GitFeatures?>(null);

    // While there is no Git to run, coming back to the window looks again, as after installing Git.
    public Task ActivatedAsync() => Finding.Runs ? Task.CompletedTask : CheckAsync(CancellationToken.None);

    // What Trag finds without the choice; the choice is forgotten only when that is a Git to run.
    public async Task<GitFinding> ForgetChoiceAsync(CancellationToken ct)
    {
        await turn.WaitAsync(ct);
        try
        {
            var found = await installations.FindAsync(null, ct);
            if (!found.Runs) return found;
            await RememberAsync(null, ct);
            Show(found);
            return found;
        }
        finally { turn.Release(); }
    }

    private async Task<GitFinding> LookAsync(IGitInstallations git, CancellationToken ct)
    {
        var chosen = await ChosenAsync(ct);
        var finding = await git.FindAsync(chosen, ct);
        if (chosen is null || finding.Runs) return finding;
        var found = await git.FindAsync(null, ct);
        if (!found.Runs) return finding;
        await RememberAsync(null, ct);
        return found;
    }

    private async Task<string?> ChosenAsync(CancellationToken ct)
    {
        if (choice is null) return null;
        try { return await choice.LoadAsync(ct); }
        catch (Exception error) when (error is not OperationCanceledException) { return null; }
    }

    private async Task RememberAsync(string? path, CancellationToken ct)
    {
        if (choice is null) return;
        try { await choice.SaveAsync(path, ct); }
        catch (Exception error) when (error is not OperationCanceledException) { }
    }

    private void Show(GitFinding finding)
    {
        Finding = finding;
        if (finding.Runs) ready.TrySetResult();
    }
}
