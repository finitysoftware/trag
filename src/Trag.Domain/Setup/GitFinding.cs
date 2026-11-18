namespace Trag.Domain.Setup;

// What looking for Git found: the Git to run, or the problem, with the Git it is about when there is one, an old one.
public sealed record GitFinding(GitProblem Problem, GitInstallation? Git = null)
{
    // Whether there is a Git to run, and that Git when looking found one.
    public bool Runs => Problem == GitProblem.None;
    public GitInstallation? Running => Runs ? Git : null;
}
