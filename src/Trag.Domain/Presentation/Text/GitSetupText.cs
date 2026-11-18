using System.Text;
using Trag.Domain.Naming;
using Trag.Domain.Setup;
using Trag.Domain.Status;

namespace Trag.Domain.Presentation.Text;

// What Trag says about the Git it runs: where to get Git, how to choose one, why there is none to run, and, in Settings,
// which one runs and what it brings.
public static class GitSetupText
{
    public static readonly Uri DownloadPage = new("https://git-scm.com/downloads");
    public const string ChooseTitle = "Choose git.exe";
    public const string ChooseFilter = "Git";
    public const string NoOtherGit = "Trag found no other Git, so it keeps running the chosen one.";

    public static string Problem(GitFinding finding) => finding.Problem switch
    {
        GitProblem.Missing => "No Git was found on this computer. Install Git " + Minimum + " or newer, or choose the git.exe you already have.",
        GitProblem.TooOld => "Git " + finding.Git!.Version.ToString(3) + " at " + finding.Git.Path + " is older than Trag works with. Install Git " + Minimum
            + " or newer, or choose the git.exe of a newer one.",
        GitProblem.NotGit => "The chosen file does not run as Git. Choose the git.exe of a Git installation, or install Git.",
        _ => ""
    };

    // A chosen Git given up: nothing to say once Trag found another to run, else that it keeps the chosen one.
    public static string Forgotten(GitFinding found) => found.Runs ? "" : NoOtherGit;

    // Settings names the program, its version and where it was found; what it brings follows once it is read.
    public static IReadOnlyList<DetailFact> Facts(GitInstallation git, GitFeatures? features) =>
    [
        new("Program", git.Path),
        new("Version", git.Version.ToString(3)),
        new("Found", git.Origin switch { GitOrigin.Path => "In a folder of the PATH", GitOrigin.InstallFolder => "In a standard install folder", _ => "Chosen by you" }),
        .. features is null ? [] : new DetailFact[]
        {
            new("Git LFS", features.Lfs ? "Installed" : "Not installed"),
            new("Credential helper", features.CredentialHelper is { } helper ? HelperName(helper) : "None"),
            new("Signs commits", features.Signing switch
            {
                CommitSigning.OpenPgp => "With a GPG key",
                CommitSigning.Ssh => "With an SSH key",
                CommitSigning.X509 => "With an X.509 certificate",
                _ => "No"
            })
        }
    ];

    private static string Minimum => GitLimits.MinimumGit.ToString(2);

    // A helper by its name, never whole, since a script or what follows a helper's name can carry a token: a script as
    // one, a program by its file without git-credential-, a name by itself. The program may be quoted, or its spaces
    // escaped as Git takes them.
    private static string HelperName(string helper)
    {
        if (helper.StartsWith('!')) return "A custom script";
        var quoted = helper.StartsWith('"');
        var program = new StringBuilder();
        for (var i = quoted ? 1 : 0; i < helper.Length; i++)
        {
            if (helper[i] == '\\' && i + 1 < helper.Length && helper[i + 1] == ' ') program.Append(helper[++i]);
            else if (quoted ? helper[i] == '"' : helper[i] == ' ') break;
            else program.Append(helper[i]);
        }
        var name = Path.GetFileNameWithoutExtension(program.ToString());
        return name.StartsWith("git-credential-", StringComparison.Ordinal) ? name["git-credential-".Length..] : name;
    }
}
