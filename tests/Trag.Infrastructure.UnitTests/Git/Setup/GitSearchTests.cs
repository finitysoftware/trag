using System.Diagnostics;
using System.Text;
using Trag.Domain.Naming;
using Trag.Domain.Setup;
using Trag.Domain.Status;
using Trag.Infrastructure.Git.Setup;

namespace Trag.Infrastructure.UnitTests.Git.Setup;

// Where Trag looks for Git and which one it takes: the folders of every PATH as it stands now, the machine's before the
// user's as Windows orders them, then the standard install folders, each once and only where the program is, searched
// away from the caller; the first Git new enough, else the first older one named as too old, else none; and a chosen file
// only when it is named as Git and answers as a new enough one.
public sealed class GitSearchTests
{
    private static readonly Version New = GitLimits.MinimumGit, Old = new(2, 30, 0);
    private static string In(string folder) => Path.Combine(folder, GitSearch.FileName);

    [Fact]
    public void WhenGitIsLookedFor_ShouldTryThePathFoldersInOrderThenTheInstallFoldersEachOnceWhereTheProgramIs()
    {
        var x = Path.Combine(Path.GetTempPath(), "x");
        var (a, b, c, gone) = (Path.Combine(x, "a"), Path.Combine(x, "b"), Path.Combine(x, "c"), Path.Combine(x, "gone"));
        string[] there = [In(a), In(b), In(c)];

        var candidates = GitSearch.Candidates([a, null, "\"" + b + "\"" + Path.PathSeparator + " " + gone + " " + Path.PathSeparator + Path.PathSeparator + a], [c, b], there.Contains);

        Assert.Equal([(In(a), GitOrigin.Path), (In(b), GitOrigin.Path), (In(c), GitOrigin.InstallFolder)], candidates);
    }

    [Fact]
    public async Task WhenSeveralGitsAnswer_ShouldTakeTheFirstNewEnoughOneAndAskNoFurther()
    {
        var asked = new List<string>();

        var finding = await GitSearch.FindAsync([("old", GitOrigin.Path), ("none", GitOrigin.Path), ("new", GitOrigin.InstallFolder), ("later", GitOrigin.InstallFolder)],
            path => { asked.Add(path); return Task.FromResult<Version?>(path switch { "old" => Old, "new" or "later" => New, _ => null }); });

        Assert.Equal(new GitFinding(GitProblem.None, new("new", New, GitOrigin.InstallFolder)), finding);
        Assert.Equal(["old", "none", "new"], asked);
    }

    [Fact]
    public async Task WhenOnlyOlderGitsAnswer_ShouldNameTheFirstOfThemAsTooOld()
    {
        var finding = await GitSearch.FindAsync([("none", GitOrigin.Path), ("old", GitOrigin.Path), ("older", GitOrigin.InstallFolder)],
            path => Task.FromResult<Version?>(path switch { "old" => Old, "older" => new(2, 20, 0), _ => null }));

        Assert.Equal(new GitFinding(GitProblem.TooOld, new("old", Old, GitOrigin.Path)), finding);
    }

    [Fact]
    public async Task WhenNoGitAnswers_ShouldFindItMissing()
    {
        Assert.Equal(new GitFinding(GitProblem.Missing), await GitSearch.FindAsync([("none", GitOrigin.Path)], _ => Task.FromResult<Version?>(null)));
        Assert.Equal(new GitFinding(GitProblem.Missing), await GitSearch.FindAsync([], _ => Task.FromResult<Version?>(New)));
    }

    // A folder of the PATH that is not a whole path would be looked in from wherever Trag was started, a place anyone could
    // leave a git.exe in; only whole paths are searched.
    [Fact]
    public void WhenThePathNamesFoldersThatAreNotWholePaths_ShouldPassThemBy()
    {
        var whole = Path.Combine(Path.GetTempPath(), "x", "whole");
        string[] there = [In("."), In("bin"), In(whole)];

        Assert.Equal([(In(whole), GitOrigin.Path)], GitSearch.Candidates([string.Join(Path.PathSeparator, ".", "bin", whole)], ["bin"], there.Contains));
    }

    // A PATH entry with characters no folder name may hold is looked in like any other and found empty, never an error
    // that would keep Git from being found in the folders after it.
    [Fact]
    public void WhenAPathEntryHasInvalidCharacters_ShouldPassItBy()
    {
        var odd = Path.Combine(Path.GetTempPath(), "x<y>|z?");

        var candidates = GitSearch.Candidates([odd + Path.PathSeparator + Path.GetTempPath()], [], File.Exists);

        Assert.DoesNotContain(candidates, candidate => candidate.Path.StartsWith(odd, StringComparison.Ordinal));
    }

    // Settings says what Git brings for every repository: the system's and the user's configuration, never that of a
    // repository the folder Git is asked from happens to be in, nor one given on its command line.
    [Fact]
    public void WhenTheConfigurationListsItsScopes_ShouldKeepOnlyTheSystemsAndTheUsers() =>
        Assert.Equal(new GitFeatures(true, "manager", CommitSigning.None), GitSearch.Features(GitSearch.Shared(Encoding.UTF8.GetBytes(
            "system\0filter.lfs.process\ngit-lfs filter-process\0global\0credential.helper\nmanager\0local\0credential.helper\nstore\0"
            + "local\0commit.gpgsign\ntrue\0command\0gpg.format\nssh\0"))));

    // A slow folder, as an offline network drive in the PATH, holds the search and not its caller, whose thread may be the
    // window's.
    [Fact]
    public async Task WhenTheFoldersAreSlowToSearch_ShouldNotHoldTheCaller()
    {
        using var release = new ManualResetEventSlim();
        using var fallback = new Timer(_ => release.Set(), null, TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan);
        var watch = Stopwatch.StartNew();

        IEnumerable<(string, GitOrigin)> Slow() { release.Wait(); yield break; }

        var finding = GitSearch.FindAsync(Slow(), _ => Task.FromResult<Version?>(New));
        var held = watch.Elapsed;
        release.Set();

        Assert.Equal(new GitFinding(GitProblem.Missing), await finding);
        Assert.True(held < TimeSpan.FromSeconds(1), $"the caller was held {held}");
    }

    // A folder after Git's, as an offline network share whose first look Windows holds for about 20 s, is never looked in
    // once the Git before it answers, as a terminal starting git never looks there either.
    [Fact]
    public async Task WhenAFolderAfterGitIsSlowToLookIn_ShouldFindGitWithoutLookingThere()
    {
        var x = Path.Combine(Path.GetTempPath(), "x");
        var (git, offline) = (Path.Combine(x, "git"), Path.Combine(x, "offline"));
        var looked = new List<string>();

        var finding = await GitSearch.FindAsync(GitSearch.Candidates([git + Path.PathSeparator + offline], [x], path => { looked.Add(path); return true; }),
            _ => Task.FromResult<Version?>(New));

        Assert.Equal(new GitFinding(GitProblem.None, new(In(git), New, GitOrigin.Path)), finding);
        Assert.Equal([In(git)], looked);
    }

    // The PATH as a new process gets it from Windows: the one Trag started with first, then the machine's before the user's.
    [Fact]
    public void WhenThePathIsReadAgain_ShouldPutTheMachinesFoldersBeforeTheUsers() =>
        Assert.Equal([Environment.GetEnvironmentVariable("PATH"), Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)], GitLocator.Paths());

    [Fact]
    public async Task WhenAFileIsChosen_ShouldTakeItOnlyWhenItIsNamedAsGitAndAnswersAsANewEnoughOne()
    {
        var asked = new List<string>();
        Task<Version?> Answer(string path)
        {
            asked.Add(path);
            return Task.FromResult<Version?>(path.Contains("old", StringComparison.Ordinal) ? Old : path.Contains("broken", StringComparison.Ordinal) ? null : New);
        }
        var other = Path.Combine("x", "notepad.exe");

        Assert.Equal(new GitFinding(GitProblem.None, new(In("good"), New, GitOrigin.Chosen)), await GitSearch.ChooseAsync(In("good"), Answer));
        Assert.Equal(new GitFinding(GitProblem.TooOld, new(In("old"), Old, GitOrigin.Chosen)), await GitSearch.ChooseAsync(In("old"), Answer));
        Assert.Equal(new GitFinding(GitProblem.NotGit), await GitSearch.ChooseAsync(In("broken"), Answer));
        Assert.Equal(new GitFinding(GitProblem.NotGit), await GitSearch.ChooseAsync(other, Answer));
        Assert.DoesNotContain(other, asked);
    }

    // What a Git brings, by its configuration outside any repository: Git LFS when its filter is set, the last credential
    // helper unless an empty one cleared the list, and how commits are signed.
    [Fact]
    public void WhenAGitsConfigurationIsRead_ShouldSayWhetherItHasLfsWhichHelperKeepsCredentialsAndHowItSigns()
    {
        Assert.Equal(new GitFeatures(true, "manager", CommitSigning.Ssh), GitSearch.Features(new([("filter.lfs.process", "git-lfs filter-process"),
            ("credential.helper", "store"), ("credential.helper", "manager"), ("commit.gpgsign", "true"), ("gpg.format", "ssh")])));
        Assert.Equal(new GitFeatures(false, null, CommitSigning.None), GitSearch.Features(new([("credential.helper", "manager"), ("credential.helper", "")])));
    }
}
