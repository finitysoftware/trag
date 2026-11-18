using System.Text;
using Trag.Domain.History;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Catalog;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Sync;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Diagnostics;

// What each Git build Trag supports prints where Trag tells an outcome by Git's words, read the way Trag reads it: a build
// that words a refusal differently turns its own row red here, before a user reads the wrong sentence. Each file of the
// corpus is one build's output for every situation below, named after its git --version and written by
// scripts/capture-failure-text-corpus.py. Only Git's own words are here: a hosting provider's, Git LFS's, OpenSSH's and
// GCM's do not change with Git's version and stay with each classifier's own tests.
public sealed class FailureTextCorpusTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Git", "Diagnostics", "FailureTextCorpus");

    private static readonly Dictionary<string, (string Message, bool RefusesCredentials)> Transfers = new()
    {
        ["transport-push-fetch-first"] = (TransportText.Behind, false),
        ["transport-push-non-fast-forward"] = (TransportText.Behind, false),
        ["transport-push-delete-current-branch"] = (TransportText.ProtectedBranch, false),
        ["transport-http-401-no-username"] = (TransportText.NoSavedCredentials, true),
        ["transport-http-401-no-password"] = (TransportText.NoSavedCredentials, true),
        ["transport-http-401-refused"] = (TransportText.AuthenticationFailed, true),
        ["transport-http-404-missing"] = (TransportText.RepositoryMissing, false),
        ["transport-http-connection-refused"] = (TransportText.Failed, false)
    };

    private static readonly Dictionary<string, string> Branches = new()
    {
        ["branch-switch-local-changes"] = BranchOutcomeText.LocalChangesInTheWay,
        ["branch-switch-untracked"] = BranchOutcomeText.UntrackedInTheWay,
        ["branch-switch-used-by-worktree"] = BranchOutcomeText.TargetInAnotherWorktree,
        ["branch-switch-create-exists"] = BranchOutcomeText.NameExists,
        ["branch-switch-index-lock"] = WriteText.IndexLocked,
        ["branch-switch-unknown-ref"] = BranchOutcomeText.Failed
    };

    private static readonly Dictionary<string, string> Stashes = new()
    {
        ["stash-apply-local-changes"] = StashOutcomeText.LocalChangesInTheWay,
        ["stash-apply-untracked-exists"] = StashOutcomeText.FilesInTheWay,
        ["stash-apply-index-lock"] = WriteText.IndexLocked,
        ["stash-push-index-lock"] = WriteText.IndexLocked,
        ["stash-untracked-held-open"] = StashOutcomeText.Failed,
        ["stash-untracked-unindexable"] = StashOutcomeText.UntrackedNotIndexable,
        ["stash-drop-unknown"] = StashOutcomeText.Failed
    };

    // Git 2.43.0 refuses a stash on a locked index without a word, so nothing it prints says the index is locked.
    private static readonly HashSet<(string Version, string Situation)> SaysNothing =
    [
        ("2.43.0.windows.1", "stash-push-index-lock"),
        ("2.43.0.windows.1", "stash-apply-index-lock")
    ];

    // The key a commit is signed with, and what Trag says when Git refuses the commit.
    private static readonly Dictionary<string, (CommitSigning Signing, string Message)> Commits = new()
    {
        ["commit-ssh-program-missing"] = (CommitSigning.Ssh, CommitOutcomeText.NotSigned(CommitSigning.Ssh)),
        ["commit-ssh-key-passphrase"] = (CommitSigning.Ssh, CommitOutcomeText.NotSigned(CommitSigning.Ssh)),
        ["commit-gpg-no-secret-key"] = (CommitSigning.OpenPgp, CommitOutcomeText.NotSigned(CommitSigning.OpenPgp)),
        ["commit-hook-refuses-signed"] = (CommitSigning.Ssh, CommitOutcomeText.Rejected)
    };

    // Whether a merge that signs its commit with an SSH key stopped because Git could not sign the commit.
    private static readonly Dictionary<string, bool> Merges = new()
    {
        ["merge-signing-refused"] = true,
        ["merge-conflicts-while-signing"] = false
    };

    private static readonly Dictionary<string, string> Clones = new()
    {
        ["clone-http-401-no-username"] = CloneText.AuthenticationFailed,
        ["clone-http-401-refused"] = CloneText.AuthenticationFailed,
        ["clone-http-403-forbidden"] = CloneText.AuthenticationFailed,
        ["clone-http-404-missing"] = CloneText.RepositoryMissing,
        ["clone-http-connection-refused"] = CloneText.Unreachable,
        ["clone-unresolvable-host"] = CloneText.Unreachable,
        ["clone-target-exists"] = CloneText.TargetExists
    };

    // Both deletes name the branch other.
    private static readonly Dictionary<string, string> BranchDeletes = new()
    {
        ["branch-delete-not-fully-merged"] = BranchOutcomeText.NotFullyMerged("other"),
        ["branch-delete-used-by-worktree"] = BranchOutcomeText.InOtherWorktree("other")
    };

    // A commit someone signed, checked where Git cannot run the check.
    private static readonly Dictionary<string, SignatureCheck> Signatures = new()
    {
        ["signature-openpgp-program-missing"] = SignatureCheck.CheckerMissing,
        ["signature-ssh-program-missing"] = SignatureCheck.CheckerMissing,
        ["signature-ssh-no-allowed-signers"] = SignatureCheck.NoAllowedSigners
    };

    // The one line a reader or writer looks for in Git's output to tell its outcome from the others.
    private static readonly Dictionary<string, Func<GitResult, bool>> Lines = new()
    {
        ["open-dubious-ownership"] = GitOwnership.Refused,
        ["probe-dubious-ownership"] = GitOwnership.Refused,
        ["probe-bare-explicit"] = GitBareRepository.Refused,
        ["pull-upstream-deleted"] = result => FetchOperation.UpstreamDeleted(GitOutputText.Details(result), "refs/heads/gone"),
        ["revert-nothing-to-commit"] = result => PickWriter.NothingToCommit(GitOutputText.Details(result)),
        ["stash-push-only-untracked"] = result => StashWriter.SavedNothing(GitOutputText.Details(result)),
        ["stash-apply-conflicts"] = result => StashWriter.Conflicted(GitOutputText.Details(result)),
        ["stash-drop-other-entry"] = result => StashWriter.DroppedId(result) is not null,
        ["tag-signing-refused"] = result => CommitSignings.TagRefused(GitOutputText.Details(result)),
        ["tag-needs-message"] = result => CommitSignings.TagNeedsMessage(GitOutputText.Details(result))
    };

    public static TheoryData<string> Versions => [.. Directory.GetFiles(Folder, "*.txt").Select(file => Path.GetFileNameWithoutExtension(file))];

    public static TheoryData<string, string> TransferRows => Rows(Transfers.Keys);
    public static TheoryData<string, string> BranchRows => Rows(Branches.Keys);
    public static TheoryData<string, string> StashRows => Rows(Stashes.Keys);
    public static TheoryData<string, string> CommitRows => Rows(Commits.Keys);
    public static TheoryData<string, string> MergeRows => Rows(Merges.Keys);
    public static TheoryData<string, string> CloneRows => Rows(Clones.Keys);
    public static TheoryData<string, string> BranchDeleteRows => Rows(BranchDeletes.Keys);
    public static TheoryData<string, string> SignatureRows => Rows(Signatures.Keys);
    public static TheoryData<string, string> LineRows => Rows(Lines.Keys);

    [Theory]
    [MemberData(nameof(TransferRows))]
    public void WhenASupportedGitRefusesATransfer_ShouldGiveTheSentenceOfThatSituation(string version, string situation) =>
        Assert.Equal(Transfers[situation].Message, TransportFailureText.Explain(Output(version, situation)));

    [Theory]
    [MemberData(nameof(TransferRows))]
    public void WhenASupportedGitRefusesATransfer_ShouldCountRefusedCredentialsOnlyForASignIn(string version, string situation) =>
        Assert.Equal(Transfers[situation].RefusesCredentials, TransportFailureText.RefusesCredentials(Output(version, situation)));

    [Theory]
    [MemberData(nameof(BranchRows))]
    public void WhenASupportedGitRefusesABranchCommand_ShouldGiveTheSentenceOfThatSituation(string version, string situation) =>
        Assert.Equal(Branches[situation], BranchFailureText.Explain(Output(version, situation)));

    [Theory]
    [MemberData(nameof(StashRows))]
    public void WhenASupportedGitRefusesAStashCommand_ShouldGiveTheSentenceOfThatSituation(string version, string situation)
    {
        var expected = SaysNothing.Contains((version, situation)) ? StashOutcomeText.Failed : Stashes[situation];
        Assert.Equal(expected, StashFailureText.Explain(Output(version, situation)));
    }

    // CommitWriter reads a refused commit through the check of the state it verified.
    [Theory]
    [MemberData(nameof(CommitRows))]
    public void WhenASupportedGitRefusesACommit_ShouldSayNotSignedOnlyWhenItCouldNotSign(string version, string situation)
    {
        var (signing, message) = Commits[situation];
        var state = new CommitStateCheck(Snapshots.Create() with { Signing = signing }, Snapshots.HeadId, "main");
        Assert.Equal(message, state.Refusal(GitOutputText.Details(Output(version, situation))));
    }

    // VerifiedMerge chooses between the merge's own pending sentence and the one for a commit Git could not sign by this.
    [Theory]
    [MemberData(nameof(MergeRows))]
    public void WhenASupportedGitStopsAMerge_ShouldSayNotSignedOnlyWhenItCouldNotSign(string version, string situation) =>
        Assert.Equal(Merges[situation], CommitSignings.Refused(CommitSigning.Ssh, GitOutputText.Details(Output(version, situation))));

    // GitCloneOperation reads the details it shows, trimmed.
    [Theory]
    [MemberData(nameof(CloneRows))]
    public void WhenASupportedGitRefusesAClone_ShouldGiveTheSentenceOfThatSituation(string version, string situation) =>
        Assert.Equal(Clones[situation], CloneFailureText.Explain(GitOutputText.Details(Output(version, situation)).Trim()));

    [Theory]
    [MemberData(nameof(BranchDeleteRows))]
    public void WhenASupportedGitKeepsABranchItWasToDelete_ShouldGiveTheSentenceOfThatSituation(string version, string situation) =>
        Assert.Equal(BranchDeletes[situation], BranchDeleteWriter.Refused("other", GitOutputText.Details(Output(version, situation))).Message);

    // CommitDetailsReader reads the record up to the NUL that ends it, beside what Git said on standard error.
    [Theory]
    [MemberData(nameof(SignatureRows))]
    public void WhenASupportedGitCannotCheckASignature_ShouldSayWhy(string version, string situation)
    {
        var (_, error, output) = Read(version).Single(row => row.Situation == situation);
        var record = Encoding.UTF8.GetBytes(output[..output.LastIndexOf('\0')]);
        Assert.Equal(Signatures[situation], CommitRecord.ParseSigned(record, error).Signature.Check);
    }

    [Theory]
    [MemberData(nameof(LineRows))]
    public void WhenASupportedGitTellsAnOutcomeApart_ShouldFindTheLineThatTellsIt(string version, string situation) =>
        Assert.True(Lines[situation](Output(version, situation)));

    [Theory]
    [MemberData(nameof(Versions))]
    public void WhenTheCorpusHoldsAGitVersion_ShouldHoldEverySituationOnce(string version)
    {
        string[] situations = [.. Transfers.Keys, .. Branches.Keys, .. Stashes.Keys, .. Commits.Keys, .. Merges.Keys, .. Clones.Keys,
            .. BranchDeletes.Keys, .. Signatures.Keys, .. Lines.Keys];
        Assert.Equal(situations.Order(), Read(version).Select(row => row.Situation).Order());
    }

    private static TheoryData<string, string> Rows(IEnumerable<string> situations)
    {
        var rows = new TheoryData<string, string>();
        foreach (var version in Versions)
            foreach (var situation in situations) rows.Add(version, situation);
        return rows;
    }

    // No classifier reads the exit code, so every row exits as a refusal does.
    private static GitResult Output(string version, string situation)
    {
        var (_, error, output) = Read(version).Single(row => row.Situation == situation);
        return new(1, Encoding.UTF8.GetBytes(output), Encoding.UTF8.GetBytes(error));
    }

    // A row is "=== <situation>", then "--- stderr" and "--- stdout", each followed by its lines; ^@ is a NUL. core.autocrlf
    // may check the files out with CRLF.
    private static List<(string Situation, string Error, string Output)> Read(string version)
    {
        var rows = new List<(string Situation, StringBuilder Error, StringBuilder Output)>();
        StringBuilder? stream = null;
        foreach (var line in File.ReadAllText(Path.Combine(Folder, version + ".txt")).ReplaceLineEndings("\n").TrimEnd('\n').Replace("^@", "\0").Split('\n'))
        {
            if (line.StartsWith("=== ", StringComparison.Ordinal)) rows.Add((line[4..], new(), new()));
            else if (line == "--- stderr") stream = rows[^1].Error;
            else if (line == "--- stdout") stream = rows[^1].Output;
            else stream!.Append(line).Append('\n');
        }
        return [.. rows.Select(row => (row.Situation, row.Error.ToString(), row.Output.ToString()))];
    }
}
