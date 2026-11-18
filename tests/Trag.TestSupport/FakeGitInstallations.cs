using Trag.Application.Ports.Git;
using Trag.Domain.Setup;
using Trag.Domain.Status;

namespace Trag.TestSupport;

// Git as the tests find it: a usable Git on the PATH unless a test says otherwise, and a chosen path answering as the
// test set it, else as no Git. Every look is recorded with the choice it was given; PathGate holds the looks along the
// PATH.
public sealed class FakeGitInstallations : IGitInstallations
{
    public static readonly GitInstallation PathGit = new(@"C:\Program Files\Git\cmd\git.exe", new(2, 55, 0), GitOrigin.Path);

    public GitFinding Found { get; set; } = new(GitProblem.None, PathGit);
    public Dictionary<string, GitFinding> Chosen { get; } = [];
    public List<string?> Looks { get; } = [];
    public AsyncGate PathGate { get; } = new();
    public GitFeatures? Features { get; set; } = new(false, null, CommitSigning.None);
    public List<GitInstallation> Described { get; } = [];

    public async Task<GitFinding> FindAsync(string? chosen, CancellationToken ct)
    {
        Looks.Add(chosen);
        if (chosen is null) await PathGate.WaitAsync(ct);
        return chosen is null ? Found : Chosen.GetValueOrDefault(chosen) ?? new GitFinding(GitProblem.NotGit);
    }

    public Task<GitFeatures?> DescribeAsync(GitInstallation git, CancellationToken ct)
    {
        Described.Add(git);
        return Task.FromResult(Features);
    }
}
