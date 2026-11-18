using System.Text.RegularExpressions;

namespace Trag.ArchitectureTests;

// What a screen shows comes from Domain's Presentation.Text, so a model, and a service beside it, holds no words of its
// own: the strings with a word left in the models (Domain and Application but for the texts, the naming rules, the errors,
// the ports and the engine) are Git's own names and formats, folder names, and messages of exceptions no screen shows.
// The list is exact, so a string that goes leaves it too.
public sealed partial class ModelTextTests
{
    private static readonly string[] ModelWords =
    [
        "Trag.Application/Catalog/CloneWorkflow.cs \"This catalog cannot clone.\"", // exception
        "Trag.Application/Catalog/LocalRepositoryCatalog.cs \"This catalog cannot check folders.\"", // exception
        "Trag.Application/Catalog/LocalRepositoryCatalog.cs \"This catalog cannot initialize repositories.\"", // exception
        "Trag.Application/Workspace/WorkspaceState.cs \"Wait for the Git operation before switching repositories.\"", // exception
        "Trag.Application/Workspace/WorkspaceState.cs \"Wait for the Git operation before closing the repository.\"", // exception
        "Trag.Domain/Accounts/AccountChoice.cs \"external\"", // the id of the choice that leaves credentials to Git
        "Trag.Domain/Accounts/AccountEndpoints.cs \"https\"", // an account talks to its host over HTTPS only
        "Trag.Domain/Accounts/AccountIdentity.cs \"account-\"", // the prefix of every account's id
        "Trag.Domain/Accounts/AccountMapping.cs \"https://api.github.com/\"", // GitHub's fixed addresses
        "Trag.Domain/Accounts/AccountMapping.cs \"https://github.com/\"",
        "Trag.Domain/Accounts/AccountMapping.cs \"https://github.com/login/device\"", // the only page a device code may name
        "Trag.Domain/Accounts/AccountMapping.cs \"https://gitlab.com\"", // the instance an account is added for by default
        "Trag.Domain/Accounts/AccountMapping.cs \"api/v4/\"", // GitLab's REST API under an instance
        "Trag.Domain/Accounts/AccountMapping.cs \"GitLab requires an HTTPS instance URL without credentials, query or fragment.\"", // exception
        "Trag.Domain/Accounts/RemoteAddress.cs \"https\"", // only an HTTPS remote is matched against an account
        "Trag.Domain/Accounts/RemoteAddress.cs \"ssh://\"", // Git's SSH scheme, whose key and agent are Git's own
        "Trag.Domain/Accounts/RemoteAddress.cs \".git\"", // the ending an address may give a project's path
        "Trag.Domain/Branches/PullRequestPage.cs \"refs/remotes/\"", // Git's namespace of remote-tracking branches
        "Trag.Domain/Branches/PullRequestPage.cs \"/compare/\"", // GitHub's page that starts a pull request
        "Trag.Domain/Branches/PullRequestPage.cs \"?expand=1\"",
        "Trag.Domain/Branches/PullRequestPage.cs \"/-/merge_requests/new?merge_request%5Bsource_branch%5D=\"", // GitLab's for a merge request
        "Trag.Domain/Branches/PullRequestPage.cs \"github.com\"", // the providers' hosts
        "Trag.Domain/Branches/PullRequestPage.cs \"gitlab.com\"",
        "Trag.Domain/Branches/PullRequestPage.cs \"gitlab.\"",
        "Trag.Domain/Branches/PullRequestPage.cs \"ssh.github.com\"", // their SSH hosts on the HTTPS port, and the web hosts they stand for
        "Trag.Domain/Branches/PullRequestPage.cs \"github.com\"",
        "Trag.Domain/Branches/PullRequestPage.cs \"altssh.gitlab.com\"",
        "Trag.Domain/Branches/PullRequestPage.cs \"gitlab.com\"",
        "Trag.Domain/Branches/PullRequestPage.cs \"https\"", // the schemes of a remote's address, and the page's
        "Trag.Domain/Branches/PullRequestPage.cs \"ssh\"",
        "Trag.Domain/Branches/PullRequestPage.cs \"https\"",
        "Trag.Domain/Branches/PullRequestPage.cs \"https://\"",
        "Trag.Domain/Catalog/CloneTargets.cs \"Repos\"", // the folder clones go to by default
        "Trag.Domain/Catalog/CloneTargets.cs \"repository\"", // a clone's folder when its name keeps no usable character
        "Trag.Domain/Diff/HunkPatch.cs \"\\\\ No newline at end of file\\n\"", // Git's patch format: the mark of a last line
        "Trag.Domain/Diff/HunkPatch.cs $\"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@\\n\"", // and a hunk's header
        "Trag.Domain/Diff/LfsPointer.cs \"ext-\"", // the Git LFS pointer format: an extension's line,
        "Trag.Domain/Diff/LfsPointer.cs \"oid sha256:\"", // its object name,
        "Trag.Domain/Diff/LfsPointer.cs \"size \"", // its size
        "Trag.Domain/Diff/LfsPointer.cs \"version https://git-lfs.github.com/spec/v1\"", // and its version line
        "Trag.Domain/GitObjectId.cs \"Invalid object ID.\"", // exception
        "Trag.Domain/History/CommitSignature.cs \"cannot exec\"", // Git's line for a program named by a path that is not there (Unix)
        "Trag.Domain/History/CommitSignature.cs \"cannot run\"", // Git's line for a program it could not start (Unix)
        "Trag.Domain/History/CommitSignature.cs \"cannot spawn\"", // the same on Windows
        "Trag.Domain/History/CommitSignature.cs \"gpg.ssh.allowedSignersFile needs to be configured\"", // Git's line for an SSH signature it has no signers to check against
        "Trag.Domain/Merge/MergeKind.cs \"pull\"", // the operation each merge names, and the reflog action that marks Trag's own
        "Trag.Domain/Merge/MergeKind.cs \"trag-pull-\"",
        "Trag.Domain/Merge/MergeKind.cs \"merge\"",
        "Trag.Domain/Merge/MergeKind.cs \"trag-merge-\"",
        "Trag.Domain/Rebase/RewriteTodo.cs \"drop\"", // the commands of Git's rebase plan
        "Trag.Domain/Rebase/RewriteTodo.cs \"fixup\"",
        "Trag.Domain/Rebase/RewriteTodo.cs \"fixup\"", // a fold Git put back, read from its plan and its current-fixups
        "Trag.Domain/Rebase/RewriteTodo.cs \"squash\"",
        "Trag.Domain/Rebase/RewriteTodo.cs \"squash \"",
        "Trag.Domain/Rebase/RewriteTodo.cs \"fixup\"", // whether the plan still folds the commit Git put back first
        "Trag.Domain/Rebase/RewriteTodo.cs \"squash\"",
        "Trag.Domain/Rebase/RewriteTodo.cs \"drop\"", // a drop above that fold replays nothing
        "Trag.Domain/Rebase/RewriteTodo.cs \"pick\"",
        "Trag.Domain/Rebase/RewriteTodo.cs \"exec git -c i18n.commitEncoding=UTF-8 commit --amend --allow-empty --no-verify --cleanup=verbatim -F \"", // a message set from a file
        "Trag.Domain/Rebase/RewriteTodo.cs \".txt\"", // the messages' file names
        "Trag.Domain/Rebase/RewriteTodo.cs \"cp \"", // the sequence editor: Trag's plan copied over Git's
        "Trag.Domain/Publish/PublishInput.cs \"origin\"", // Git's usual remote name, the default for a new one
        "Trag.Domain/Publish/PublishInput.cs \"Choose a remote to publish to.\"", // exception
        "Trag.Domain/Stash/StashEntry.cs \"stash@{\"", // Git's name for a stash
        "Trag.Domain/Stash/StashEntry.cs \"WIP on \"", // the prefixes Git gives a stash message, read twice
        "Trag.Domain/Stash/StashEntry.cs \"On \"",
        "Trag.Domain/Stash/StashEntry.cs \"WIP on \"",
        "Trag.Domain/Stash/StashEntry.cs \"On \"",
        "Trag.Domain/Status/CommitSignings.cs \"ssh\"", // Git's name of the SSH signing format
        "Trag.Domain/Status/CommitSignings.cs \"failed to write commit object\"", // Git's line for a commit it could not write, as when it could not sign
        "Trag.Domain/Status/CommitSignings.cs \"unable to sign the tag\"", // Git's line for a tag it could not sign
        "Trag.Domain/Status/CommitSignings.cs \"no tag message?\"", // Git's line for a tag it wanted a message for
        "Trag.Domain/Status/RepositorySnapshot.cs \"origin\"", // the remote preferred when there are several
        "Trag.Domain/Sync/RemoteRefspecMapping.cs \"refs/heads/\"", // a fetch refspec maps local branches
        "Trag.Domain/Sync/RemoteRefspecMapping.cs \"refs/remotes/\"", // to a remote's remote-tracking refs
        "Trag.Domain/Updates/FinitySoftware.cs \"finitysoftware.io\"", // the domain Trag trusts for its site and downloads
    ];

    private static readonly string[] NotModels =
        ["Trag.Domain/Presentation/", "Trag.Domain/Naming/", "Trag.Domain/Errors/", "Trag.Application/Ports/"];

    [Fact]
    public void ModelsHoldNoWordsOfTheScreen()
    {
        var words = RepositoryRoot.ProductionSources("Trag.Domain").Concat(RepositoryRoot.ProductionSources("Trag.Application"))
            .Where(file => !NotModels.Any(folder => file.Display.StartsWith(folder, StringComparison.Ordinal)))
            .SelectMany(file => CSharpSource.StringLiterals(file.Text).Where(literal => Word().IsMatch(Escape().Replace(literal, "")))
                .Select(literal => file.Display + " " + literal))
            .ToArray();
        Assert.Equal(ModelWords.Order(StringComparer.Ordinal), words.Order(StringComparer.Ordinal));
    }

    // An adapter speaks Git's language, so the rule above would list every argument it passes; what it must not hold is a
    // sentence of the screen. What stays is Git's own text (output Trag reads, a prompt it answers) and the message of an
    // exception no screen shows. An AccountException's message is the sentence Settings and Publish show, so it is not one.
    private static readonly string[] AdapterSentences =
    [
        "Trag.Infrastructure/Git/Parsing/FileDiffParser.cs \"Binary files \"", // Git's text: a binary file in a patch
        "Trag.Infrastructure/Git/Parsing/FileDiffParser.cs \"\\\\ No newline at end of file\"", // Git's text: a patch's last line without a newline
        "Trag.Infrastructure/Git/Parsing/FileDiffParser.cs \"new file mode \"", // Git's text: the headers of a patch without a line of content
        "Trag.Infrastructure/Git/Parsing/FileDiffParser.cs \"deleted file mode \"",
        "Trag.Infrastructure/Git/Parsing/FileDiffParser.cs \"old mode \"",
        "Trag.Infrastructure/Git/Parsing/FileDiffParser.cs \"new mode \"",
        "Trag.Infrastructure/Git/Parsing/GitVersion.cs \"git version \"", // Git's text: what git --version prints first
        "Trag.Infrastructure/Git/Parsing/PatchHunk.cs \"diff --git \"", // Git's text: a patch's file header
        "Trag.Infrastructure/Git/Read/DiffSizeInspector.cs \" from promisor remote\"", // Git's text: a version a partial clone lacks, before Git 2.45
        "Trag.Infrastructure/Git/Diagnostics/GitOwnership.cs \"dubious ownership\"", // Git's text: the safe.directory refusal
        "Trag.Infrastructure/Git/Diagnostics/GitBareRepository.cs \"cannot use bare repository\"", // Git's text: the safe.bareRepository refusal
        "Trag.Infrastructure/Git/Diagnostics/GitRepositoryFormat.cs \"unknown repository extension\"", // Git's text: a repository format this Git does not read
        "Trag.Infrastructure/Git/Catalog/CloneFailureText.cs \"could not create work tree dir\"", // Git's text: clone failures by cause
        "Trag.Infrastructure/Git/Catalog/CloneFailureText.cs \"Authentication failed\"",
        "Trag.Infrastructure/Git/Catalog/CloneFailureText.cs \"Permission denied\"",
        "Trag.Infrastructure/Git/Catalog/CloneFailureText.cs \"could not read Username\"",
        "Trag.Infrastructure/Git/Catalog/CloneFailureText.cs \"returned error: 403\"",
        "Trag.Infrastructure/Git/Catalog/CloneFailureText.cs \"Could not resolve host\"",
        "Trag.Infrastructure/Git/Catalog/CloneFailureText.cs \"unable to access\"",
        "Trag.Infrastructure/Git/Catalog/CloneFailureText.cs \"already exists\"",
        "Trag.Infrastructure/Git/Processes/GitTransferProgress.cs \"Enumerating objects:\"", // Git's text: the phases of a transfer
        "Trag.Infrastructure/Git/Processes/GitTransferProgress.cs \"Counting objects:\"",
        "Trag.Infrastructure/Git/Processes/GitTransferProgress.cs \"Compressing objects:\"",
        "Trag.Infrastructure/Git/Processes/GitTransferProgress.cs \"Receiving objects:\"",
        "Trag.Infrastructure/Git/Processes/GitTransferProgress.cs \"Resolving deltas:\"",
        "Trag.Infrastructure/Git/Processes/GitTransferProgress.cs \"Writing objects:\"",
        "Trag.Infrastructure/Git/Processes/GitCommandException.cs $\"Git failed with exit code {exitCode}.\"", // exception, shown only among the details
        "Trag.Infrastructure/Git/Processes/GitOutputLimitException.cs \"Git output exceeded the configured limit.\"", // exception
        "Trag.Infrastructure/Platform/Secrets/SecretId.cs \"Invalid account ID.\"", // exception
        "Trag.Infrastructure/Platform/Secrets/SecretStoreException.cs $\"Secret store is {status}.\"", // exception; the vault answers it with its own sentence
        "Trag.Infrastructure/Git/Write/BranchDeleteWriter.cs \"not fully merged\"", // Git's text: why a branch delete was refused
        "Trag.Infrastructure/Git/Write/BranchDeleteWriter.cs \"used by worktree\"",
        "Trag.Infrastructure/Git/Write/BranchFailureText.cs \"untracked working tree files would be overwritten\"", // Git's text: a switch or an apply refused by its cause
        "Trag.Infrastructure/Git/Write/BranchFailureText.cs \"would lose untracked files\"",
        "Trag.Infrastructure/Git/Write/BranchFailureText.cs \"would be overwritten\"",
        "Trag.Infrastructure/Git/Write/BranchFailureText.cs \"commit your changes or stash them\"",
        "Trag.Infrastructure/Git/Write/BranchFailureText.cs \"used by worktree\"",
        "Trag.Infrastructure/Git/Write/BranchFailureText.cs \"already exists\"",
        "Trag.Infrastructure/Git/Write/FileVersionWriter.cs \"old mode \"", // Git's text: a patch line naming a file's mode before the change
        "Trag.Infrastructure/Git/Write/StashFailureText.cs \"untracked working tree files would be overwritten\"", // Git's text: an apply refused by an untracked file
        "Trag.Infrastructure/Git/Write/StashFailureText.cs \"would be overwritten\"",
        "Trag.Infrastructure/Git/Write/StashFailureText.cs \"commit your changes or stash them\"",
        "Trag.Infrastructure/Git/Write/StashFailureText.cs \"already exists\"",
        "Trag.Infrastructure/Git/Write/StashFailureText.cs \"could not write index\"", // Git's text: a stash on a locked index, before Git 2.55
        "Trag.Infrastructure/Git/Write/StashFailureText.cs \"Permission denied\"", // Git's text: a file another program holds
        "Trag.Infrastructure/Git/Write/StashFailureText.cs \"unable to index file\"", // Git's text: an untracked file Git cannot index
        "Trag.Infrastructure/Git/Write/StashWriter.cs \"No local changes to save\"",
        "Trag.Infrastructure/Git/Write/PickWriter.cs \"'prepare-commit-msg' hook failed\"", // Git's text: the hook refused the pick's commit
        "Trag.Infrastructure/Git/Write/PickWriter.cs \"nothing to commit\"", // Git's text: a revert with nothing left to undo
        "Trag.Infrastructure/Git/Sync/RemoteReadException.cs \"The push destination could not be read.\"", // exception; its callers answer with the transport's sentence
        "Trag.Infrastructure/Git/Sync/TransportEnvironment.cs \" -oBatchMode=yes -oConnectTimeout=15 -oServerAliveInterval=15 -oServerAliveCountMax=2\"", // ssh's options, not text
        "Trag.Infrastructure/Git/Sync/FetchOperation.cs \"couldn't find remote ref \"", // Git's text: a pull whose upstream the remote no longer has
        "Trag.Infrastructure/Git/Sync/TransportFailureText.cs \"Authentication failed\"", // Git's text: a failed transfer by its cause
        "Trag.Infrastructure/Git/Sync/TransportFailureText.cs \"Permission denied (\"", // OpenSSH's text: a key the server refused
        "Trag.Infrastructure/Git/Sync/TransportFailureText.cs \"could not read Username\"",
        "Trag.Infrastructure/Git/Sync/TransportFailureText.cs \"could not read Password\"",
        "Trag.Infrastructure/Git/Sync/TransportFailureText.cs \"terminal prompts disabled\"",
        "Trag.Infrastructure/Git/Sync/TransportFailureText.cs \"Cannot prompt because\"",
        "Trag.Infrastructure/Git/Sync/TransportFailureText.cs \"fetch first\"",
        "Trag.Infrastructure/Git/Sync/TransportFailureText.cs @\"\\[rejected\\]\\s+(?<tag>\\S+)\\s+->\\s+\\S+\\s+\\(would clobber existing tag\\)\"", // Git's line for a tag a fetch of all tags would overwrite here
        "Trag.Infrastructure/Git/Sync/TransportFailureText.cs \"refusing to delete the current branch\"",
        "Trag.Infrastructure/Git/Sync/TransportFailureText.cs \"protected branch\"",
        "Trag.Infrastructure/Git/Sync/TransportFailureText.cs \"without `workflow` scope\"",
        "Trag.Infrastructure/Git/Sync/TransportFailureText.cs \"OAuth App access restrictions\"",
        "Trag.Infrastructure/Git/Sync/TransportFailureText.cs \"repository '[^']*' not found|Repository or object not found\"", // Git's and Git LFS's lines for an address its HTTPS host answered with 404
        "Trag.Infrastructure/Accounts/Credentials/CredentialPrompt.cs \"Username for '\"", // Git's text: the prompts the askpass helper answers
        "Trag.Infrastructure/Accounts/Credentials/CredentialPrompt.cs \"Password for '\"",
        "Trag.Infrastructure/Accounts/Providers/GitHubDeviceLoginClient.cs \"repo workflow admin:org admin:org_hook admin:repo_hook admin:public_key admin:gpg_key delete_repo gist notifications user offline_access\"", // GitHub's scope names, not text
    ];

    private static readonly string[] ShownExceptions = ["AccountException"];

    [Fact]
    public void AdaptersHoldNoSentencesOfTheScreen()
    {
        var sentences = RepositoryRoot.ProductionSources("Trag.Infrastructure")
            .SelectMany(file =>
            {
                var code = CSharpSource.Strip(file.Text);
                return CSharpSource.StringLiteralsAt(file.Text)
                    .Where(literal => Sentence().IsMatch(Escape().Replace(Hole().Replace(literal.Text, ""), "")) && !IsHiddenExceptionMessage(code, literal.Start))
                    .Select(literal => file.Display + " " + literal.Text);
            })
            .ToArray();
        Assert.Equal(AdapterSentences.Order(StringComparer.Ordinal), sentences.Order(StringComparer.Ordinal));
    }

    // The literal is the first argument of a new exception whose message no screen shows.
    private static bool IsHiddenExceptionMessage(string code, int start)
    {
        var made = NewException().Match(code[Math.Max(0, start - 200)..start]);
        return made.Success && !ShownExceptions.Contains(made.Groups["type"].Value);
    }

    // Two letters in a row make a word; an escape such as \r or \n does not.
    [GeneratedRegex("[A-Za-z]{2}")]
    private static partial Regex Word();
    [GeneratedRegex(@"\\.")]
    private static partial Regex Escape();
    // Two words with a space between them make a sentence.
    [GeneratedRegex(@"[A-Za-z]{2}\S*\s+\S*[A-Za-z]{2}")]
    private static partial Regex Sentence();
    // What an interpolated literal fills in is not its text.
    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex Hole();
    [GeneratedRegex(@"new\s+(?<type>\w+Exception)\(\s*$")]
    private static partial Regex NewException();
}
