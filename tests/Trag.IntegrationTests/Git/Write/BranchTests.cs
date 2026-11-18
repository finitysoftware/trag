using Trag.Application.Live;
using Trag.Application.Ports.Threading;
using Trag.Application.Workspace;
using Trag.Domain.Merge;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Sync;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

public sealed class BranchTests
{
    [Fact]
    public async Task WhenAClonedRepositoryIsRead_ShouldListTheCurrentBranchItsUpstreamAndEveryRemoteBranch()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var work = await Clone(fixture); var ct = fixture.Token;
        var branchReader = new CliBranchReader(fixture.Runner);
        var branches = await branchReader.ReadBranchesAsync(work, ct);
        Assert.Equal("main", branches.Current);
        Assert.Equal(["main"], branches.Local.Select(branch => branch.Name));
        Assert.Equal("refs/remotes/origin/main", branches.Local[0].Upstream);
        Assert.Equal(["origin/main", "origin/release", "origin/topic"], branches.Remote.Select(branch => branch.Name));
        Assert.All(branches.Remote, branch => Assert.Equal("origin", branch.Remote));
    }

    // origin now fetches refs/heads/* into refs/remotes/origin/feature/*: each remote branch is named as origin has it, and
    // the refs the clone's mapping wrote, which no mapping writes any more, are no branch of origin.
    [Fact]
    public async Task WhenTheFetchMappingRenamesBranches_ShouldNameEachRemoteBranchAsTheRemoteHasIt()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var work = await Clone(fixture); var ct = fixture.Token;
        await fixture.GitAtAsync(work, "config", "remote.origin.fetch", "+refs/heads/*:refs/remotes/origin/feature/*");
        await fixture.GitAtAsync(work, "fetch", "--quiet", "origin");

        var branches = await new CliBranchReader(fixture.Runner).ReadBranchesAsync(work, ct);

        Assert.Equal([("origin/feature/main", "main"), ("origin/feature/release", "release"), ("origin/feature/topic", "topic"),
            ("origin/main", null), ("origin/release", null), ("origin/topic", null)], branches.Remote.Select(branch => (branch.Name, branch.NameOnRemote)));
    }

    // The refusals: a bad name, a taken name, a switch to a branch that does not exist and a start point that does not.
    [Fact]
    public async Task WhenABranchIsCreatedAtARemoteBranchWithoutSwitching_ShouldStayOnTheCurrentOneAndRefuseWhatGitCannotDo()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var work = await Clone(fixture); var ct = fixture.Token; var runner = fixture.Runner;
        Task<string> Git(string folder, params string[] arguments) => fixture.GitAtAsync(folder, arguments);
        var topicHead = await Git(work, "rev-parse", "refs/remotes/origin/topic");

        var reader = new CliRepositoryReader(runner); var writer = new GitWriteRouter(runner);
        var snapshot = await reader.ReadAsync(work, ct);
        var created = await writer.ExecuteAsync(new BranchRequest(snapshot, new("feature/x", "refs/remotes/origin/topic", Switch: false), BranchAction.Create), ct);
        Assert.True(created.Outcome == GitWriteOutcome.Succeeded, created.Message + " " + created.Details);
        Assert.Equal("main", (await reader.ReadAsync(work, ct)).Branch);
        Assert.Equal(topicHead, await Git(work, "rev-parse", "refs/heads/feature/x"));
        Assert.Equal(GitWriteOutcome.Rejected, (await writer.ExecuteAsync(new BranchRequest(snapshot, new("bad..name"), BranchAction.Create), ct)).Outcome);
        Assert.Equal(GitWriteOutcome.Rejected, (await writer.ExecuteAsync(new BranchRequest(snapshot, new("feature/x"), BranchAction.Create), ct)).Outcome);
        Assert.Equal(GitWriteOutcome.Rejected, (await writer.ExecuteAsync(new BranchRequest(snapshot, new("nope"), BranchAction.Switch), ct)).Outcome);
        Assert.Equal(GitWriteOutcome.Rejected, (await writer.ExecuteAsync(new BranchRequest(snapshot, new("feature/y", "refs/heads/missing"), BranchAction.Create), ct)).Outcome);
    }

    // feature/x and the refusals of the test above come first, as they did before the split.
    [Fact]
    public async Task WhenSwitchingWithLocalChanges_ShouldCarryTheOnesGitAllowsAndRefuseTheOnesTheTargetWouldOverwrite()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var work = await Clone(fixture); var ct = fixture.Token; var runner = fixture.Runner;
        var reader = new CliRepositoryReader(runner); var writer = new GitWriteRouter(runner);
        await CreateFeature(fixture, reader, writer, work);

        // A change to a file that is identical on both branches travels with the switch, as Git itself allows.
        await File.WriteAllTextAsync(Path.Combine(work, "readme.txt"), "shared\nlocal edit\n", ct);
        var snapshot = await reader.ReadAsync(work, ct);
        var switched = await writer.ExecuteAsync(new BranchRequest(snapshot, new("feature/x"), BranchAction.Switch), ct);
        Assert.True(switched.Outcome == GitWriteOutcome.Succeeded, switched.Message + " " + switched.Details);
        Assert.Equal("feature/x", (await reader.ReadAsync(work, ct)).Branch);
        Assert.Equal("shared\nlocal edit\n", await File.ReadAllTextAsync(Path.Combine(work, "readme.txt"), ct));
        Assert.True(File.Exists(Path.Combine(work, "topic.txt")));

        // An edit the target branch would overwrite is refused and kept untouched.
        await File.WriteAllTextAsync(Path.Combine(work, "topic.txt"), "edited\n", ct);
        snapshot = await reader.ReadAsync(work, ct);
        var refused = await writer.ExecuteAsync(new BranchRequest(snapshot, new("main"), BranchAction.Switch), ct);
        Assert.Equal(GitWriteOutcome.Rejected, refused.Outcome); Assert.Contains("would be overwritten", refused.Message);
        Assert.Equal("feature/x", (await reader.ReadAsync(work, ct)).Branch);
        Assert.Equal("edited\n", await File.ReadAllTextAsync(Path.Combine(work, "topic.txt"), ct));
    }

    // An untracked file in the target's way is no local change a commit or a plain stash moves aside, so the refusal says
    // untracked and the file stays as it is.
    [Fact]
    public async Task WhenSwitchingWouldOverwriteAnUntrackedFile_ShouldRefuseAndSayUntracked()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var work = await Clone(fixture); var ct = fixture.Token; var runner = fixture.Runner;
        var reader = new CliRepositoryReader(runner); var writer = new GitWriteRouter(runner);
        await fixture.GitAtAsync(work, "branch", "topic", "refs/remotes/origin/topic");
        await File.WriteAllTextAsync(Path.Combine(work, "topic.txt"), "mine\n", ct);

        var refused = await writer.ExecuteAsync(new BranchRequest(await reader.ReadAsync(work, ct), new("topic"), BranchAction.Switch), ct);

        Assert.Equal(GitWriteOutcome.Rejected, refused.Outcome);
        Assert.Equal(Trag.Domain.Presentation.Text.BranchOutcomeText.UntrackedInTheWay, refused.Message);
        Assert.Equal("main", (await reader.ReadAsync(work, ct)).Branch);
        Assert.Equal("mine\n", await File.ReadAllTextAsync(Path.Combine(work, "topic.txt"), ct));
    }

    // An ignored file is the user's own as much as an untracked one (a .env, a local config, a log): a switch to a branch,
    // or to a new branch started there or tracking it, that has a file at its path, or at the path of a folder holding
    // only ignored files, is refused like one over an untracked file, and the ignored file stays as it was.
    [Theory]
    [InlineData("build.log", "switch")]
    [InlineData("build.log", "new branch")]
    [InlineData("build.log", "tracking branch")]
    [InlineData("out/mine.log", "switch")]
    public async Task WhenAnIgnoredFileStandsWhereTheTargetTracksAFile_ShouldRefuseAndKeepIt(string ignored, string move)
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var ct = fixture.Token; var root = fixture.Root;
        var reader = new CliRepositoryReader(fixture.Runner); var writer = new GitWriteRouter(fixture.Runner);
        await fixture.WriteAsync("readme.txt", "readme\n");
        await fixture.CommitAsync("Main", "readme.txt");
        await fixture.GitAsync("switch", "-c", "topic");
        await fixture.WriteAsync("build.log", "theirs\n");
        await fixture.WriteAsync("out", "theirs\n");
        await fixture.CommitAsync("Topic", "build.log", "out");
        await fixture.GitAsync("switch", "main");
        await fixture.GitAsync("remote", "add", "origin", root);
        await fixture.GitAsync("fetch", "origin");
        await File.WriteAllTextAsync(Path.Combine(root, ".git", "info", "exclude"), "*.log\n", ct);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, ignored))!);
        await File.WriteAllTextAsync(Path.Combine(root, ignored), "mine\n", ct);
        var (operation, action) = move switch
        {
            "switch" => (new Trag.Domain.Branches.BranchOperation("topic"), BranchAction.Switch),
            "new branch" => (new("feature", "topic"), BranchAction.Create),
            _ => (new("feature", null, true, "refs/remotes/origin/topic"), BranchAction.Create)
        };

        var refused = await writer.ExecuteAsync(new BranchRequest(await reader.ReadAsync(root, ct), operation, action), ct);

        Assert.Equal((GitWriteOutcome.Rejected, Trag.Domain.Presentation.Text.BranchOutcomeText.UntrackedInTheWay), (refused.Outcome, refused.Message));
        Assert.Equal("main", (await reader.ReadAsync(root, ct)).Branch);
        Assert.Equal("mine\n", await File.ReadAllTextAsync(Path.Combine(root, ignored), ct));
    }

    // A merge in progress keeps every switch back at the writer too, not only in the dialog that would have sent it.
    [Fact]
    public async Task WhenSwitchingDuringAMergeInProgress_ShouldBeRefusedWithStateUnsupported()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var work = await Clone(fixture); var ct = fixture.Token; var runner = fixture.Runner;
        var reader = new CliRepositoryReader(runner); var writer = new GitWriteRouter(runner);
        foreach (var (key, value) in new[] { ("user.name", "Trag Fixture"), ("user.email", "fixture@example.invalid"), ("commit.gpgsign", "false") })
            await fixture.GitAtAsync(work, "config", key, value);
        await File.WriteAllTextAsync(Path.Combine(work, "readme.txt"), "shared\nmain side\n", ct);
        await fixture.GitAtAsync(work, "commit", "-am", "Main side");
        await fixture.GitAtAsync(work, "switch", "-c", "side", "HEAD~1");
        await File.WriteAllTextAsync(Path.Combine(work, "readme.txt"), "shared\nother side\n", ct);
        await fixture.GitAtAsync(work, "commit", "-am", "Other side");
        await fixture.GitAtAsync(work, "switch", "main");
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.GitAtAsync(work, "merge", "--no-ff", "side"));
        var snapshot = await reader.ReadAsync(work, ct);
        Assert.Equal(Trag.Domain.Status.RepositoryOperation.Merge, snapshot.Operation);

        var refused = await writer.ExecuteAsync(new BranchRequest(snapshot, new("side"), BranchAction.Switch), ct);

        Assert.Equal((GitWriteOutcome.Rejected, Trag.Domain.Presentation.Text.WriteText.StateUnsupported), (refused.Outcome, refused.Message));
        Assert.Equal("main", (await reader.ReadAsync(work, ct)).Branch);
    }

    // origin/release offered from the list when a local release already tracks something else: the switch goes to the
    // local branch of that name, as Git's own switch would, and nothing is created or re-pointed.
    [Fact]
    public async Task WhenSwitchingToARemoteBranchWhoseLocalNameExistsWithAnotherUpstream_ShouldSwitchToTheLocalOne()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var work = await Clone(fixture); var ct = fixture.Token; var runner = fixture.Runner;
        var reader = new CliRepositoryReader(runner); var writer = new GitWriteRouter(runner); var branchReader = new CliBranchReader(runner);
        await fixture.GitAtAsync(work, "branch", "--track", "release", "refs/remotes/origin/topic");
        var catalog = Trag.Domain.Branches.BranchCatalog.Create(await branchReader.ReadBranchesAsync(work, ct), await reader.ReadAsync(work, ct));
        var move = Trag.Domain.Branches.BranchMoves.SwitchTo(catalog.Remote.Single(branch => branch.Name == "origin/release"), catalog)!;

        var switched = await writer.ExecuteAsync(new BranchRequest(await reader.ReadAsync(work, ct), move.Operation, move.Action), ct);

        Assert.True(switched.Outcome == GitWriteOutcome.Succeeded, switched.Message + " " + switched.Details);
        var branches = await branchReader.ReadBranchesAsync(work, ct);
        Assert.Equal("release", branches.Current);
        Assert.Equal("refs/remotes/origin/topic", branches.Local.Single(branch => branch.Name == "release").Upstream);
    }

    // A name Git takes is written whole, whatever letters, dots and slashes it holds: created and switched to, renamed,
    // and deleted once the switch went back.
    [Fact]
    public async Task WhenABranchNameHasUnicodeDotsAndSlashes_ShouldCreateSwitchRenameAndDeleteIt()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var work = await Clone(fixture); var ct = fixture.Token; var runner = fixture.Runner;
        var reader = new CliRepositoryReader(runner); var writer = new GitWriteRouter(runner);
        const string name = "feature/šđž.v1.2", renamed = "feature/ćč-ü.v2";

        var created = await writer.ExecuteAsync(new BranchRequest(await reader.ReadAsync(work, ct), new(name), BranchAction.Create), ct);
        Assert.True(created.Outcome == GitWriteOutcome.Succeeded, created.Message + " " + created.Details);
        Assert.Equal(name, (await reader.ReadAsync(work, ct)).Branch);
        var moved = await writer.ExecuteAsync(new BranchRenameRequest(await reader.ReadAsync(work, ct), name, renamed), ct);
        Assert.True(moved.Outcome == GitWriteOutcome.Succeeded, moved.Message + " " + moved.Details);
        Assert.Equal(renamed, (await reader.ReadAsync(work, ct)).Branch);
        await writer.ExecuteAsync(new BranchRequest(await reader.ReadAsync(work, ct), new("main"), BranchAction.Switch), ct);
        var deleted = await writer.ExecuteAsync(new BranchRequest(await reader.ReadAsync(work, ct), new(renamed), BranchAction.Delete), ct);

        Assert.True(deleted.Outcome == GitWriteOutcome.Succeeded, deleted.Message + " " + deleted.Details);
        Assert.Equal("main", await fixture.GitAtAsync(work, "branch", "--format=%(refname:short)"));
    }

    // A branch named like a commit id, or like a tag, is still the branch to a switch: HEAD lands on the branch and its
    // commit, not on the commit the id names or the tag points at.
    [Theory]
    [InlineData("sha")]
    [InlineData("tag")]
    public async Task WhenABranchIsNamedLikeAFullShaOrATag_ShouldSwitchToTheBranch(string kind)
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var work = await Clone(fixture); var ct = fixture.Token; var runner = fixture.Runner;
        var reader = new CliRepositoryReader(runner); var writer = new GitWriteRouter(runner);
        var (main, topic) = (await fixture.GitAtAsync(work, "rev-parse", "HEAD"), await fixture.GitAtAsync(work, "rev-parse", "refs/remotes/origin/topic"));
        var name = kind == "sha" ? topic : "v1.0";
        if (kind == "tag") await fixture.GitAtAsync(work, "tag", "v1.0", topic);
        await writer.ExecuteAsync(new BranchRequest(await reader.ReadAsync(work, ct), new(name, Switch: false), BranchAction.Create), ct);

        var switched = await writer.ExecuteAsync(new BranchRequest(await reader.ReadAsync(work, ct), new(name), BranchAction.Switch), ct);

        Assert.True(switched.Outcome == GitWriteOutcome.Succeeded, switched.Message + " " + switched.Details);
        var snapshot = await reader.ReadAsync(work, ct);
        Assert.Equal((name, main), (snapshot.Branch, snapshot.Head));
    }

    // A name Git takes reaches the remote as it is: published, pushed to and deleted there under that name, beside a tag of
    // the same name the remote has too, which stays; then renamed here, to a name as long, and deleted. The names are
    // letters, dots and slashes, a full commit id, a tag's name, and 255 characters, the longest the dialog takes, in
    // parts Windows can store. Git does not hand Trag's core.longpaths to the receiving side of a push to a folder, so the
    // remote in its long folder has its own, as a server on Windows would need.
    [Theory]
    [InlineData("unicode")]
    [InlineData("sha")]
    [InlineData("tag")]
    [InlineData("255 characters")]
    public async Task WhenABranchWithAnUnusualNameIsPublishedPushedAndDeletedOnTheRemote_ShouldUseThatNameThere(string kind)
    {
        using var f = await Trag.IntegrationTests.Git.Sync.SyncFixture.Create();
        var ct = f.Token; var writer = new GitWriteRouter(new(), f.Sync);
        await f.Git(f.Bare, "config", "core.longpaths", "true");
        await f.Git(f.One, "tag", "v1.0");
        await f.Git(f.One, "push", "origin", "refs/tags/v1.0");
        var name = kind switch { "unicode" => "feature/šđž.v1.2", "sha" => await f.Head(f.One), "tag" => "v1.0", _ => "long/" + new string('b', 250) };
        var created = await writer.ExecuteAsync(new BranchRequest(await f.Reader.ReadAsync(f.One, ct), new(name), BranchAction.Create), ct);
        Assert.True(created.Outcome == GitWriteOutcome.Succeeded, created.Message + " " + created.Details);
        await f.Commit(f.One, "published");

        var published = await writer.ExecuteAsync(new PublishRequest(await f.Reader.ReadAsync(f.One, ct), Trag.Domain.Publish.PublishOperation.PushTo("origin", name, null)), ct);
        Assert.True(published.Outcome == GitWriteOutcome.Succeeded, published.Message + " " + published.Details);
        await f.Commit(f.One, "pushed");
        var pushed = await f.Execute(SyncAction.Push);
        Assert.True(pushed.Outcome == GitWriteOutcome.Succeeded, pushed.Message + " " + pushed.Details);
        Assert.Equal(await f.Head(f.One), await f.Git(f.Bare, "rev-parse", "refs/heads/" + name));
        await writer.ExecuteAsync(new BranchRequest(await f.Reader.ReadAsync(f.One, ct), new("trunk"), BranchAction.Switch), ct);
        var deleted = await writer.ExecuteAsync(new RemoteBranchDeleteRequest(await f.Reader.ReadAsync(f.One, ct), "origin", name), ct);

        Assert.True(deleted.Outcome == GitWriteOutcome.Succeeded, deleted.Message + " " + deleted.Details);
        Assert.Equal("refs/heads/trunk\nrefs/tags/v1.0", await f.Git(f.Bare, "for-each-ref", "--format=%(refname)"));
        var renamed = name[..^1] + "x";
        var moved = await writer.ExecuteAsync(new BranchRenameRequest(await f.Reader.ReadAsync(f.One, ct), name, renamed), ct);
        Assert.True(moved.Outcome == GitWriteOutcome.Succeeded, moved.Message + " " + moved.Details);
        var tip = await f.Git(f.One, "rev-parse", "refs/heads/" + renamed);
        var removed = await writer.ExecuteAsync(new BranchRequest(await f.Reader.ReadAsync(f.One, ct), new(renamed, ForceAt: tip), BranchAction.Delete), ct);
        Assert.True(removed.Outcome == GitWriteOutcome.Succeeded, removed.Message + " " + removed.Details);
        Assert.Equal("refs/heads/trunk", await f.Git(f.One, "for-each-ref", "--format=%(refname)", "refs/heads"));
    }

    // The branch writes of the tests above come first, as they did before the split: the tracking branch is created from
    // feature/x, with the edit to readme.txt still carried along.
    [Fact]
    public async Task WhenARemoteBranchIsChosen_ShouldCreateALocalBranchThatTracksItAndSwitchToIt()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var work = await Clone(fixture); var ct = fixture.Token; var runner = fixture.Runner;
        var reader = new CliRepositoryReader(runner); var writer = new GitWriteRouter(runner); var branchReader = new CliBranchReader(runner);
        await CreateFeature(fixture, reader, writer, work);
        await SwitchWithLocalChanges(fixture, reader, writer, work);

        var snapshot = await reader.ReadAsync(work, ct);
        var tracked = await writer.ExecuteAsync(new BranchRequest(snapshot, new("release", null, true, "refs/remotes/origin/release"), BranchAction.Create), ct);
        Assert.True(tracked.Outcome == GitWriteOutcome.Succeeded, tracked.Message + " " + tracked.Details);
        var branches = await branchReader.ReadBranchesAsync(work, ct);
        Assert.Equal("release", branches.Current);
        Assert.Equal("refs/remotes/origin/release", branches.Local.Single(branch => branch.Name == "release").Upstream);
    }

    // The parents are what the graph is drawn from. The branch writes of the tests above come first, as they did before
    // the split, so the current branch is release.
    [Fact]
    public async Task WhenTheHistoryOfAllBranchesIsRead_ShouldIncludeCommitsOutsideTheCurrentBranchWithTheirParents()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var work = await Clone(fixture); var ct = fixture.Token; var runner = fixture.Runner;
        Task<string> Git(string folder, params string[] arguments) => fixture.GitAtAsync(folder, arguments);
        var reader = new CliRepositoryReader(runner); var writer = new GitWriteRouter(runner);
        await CreateFeature(fixture, reader, writer, work);
        await SwitchWithLocalChanges(fixture, reader, writer, work);
        await TrackRelease(fixture, reader, writer, work);

        var history = new CliHistoryReader(runner);
        var head = await Git(work, "rev-parse", "HEAD");
        var everything = await history.ReadPageAsync(work, head, new("", 0, true), ct);
        Assert.Contains(everything.Commits, commit => commit.Subject == "Topic only" && commit.Parents.Count == 1);
        Assert.Contains(everything.Commits, commit => commit.Subject == "Initial" && commit.Parents.Count == 0);
        var current = await history.ReadPageAsync(work, head, new("", 0, false), ct);
        Assert.DoesNotContain(current.Commits, commit => commit.Subject == "Topic only");
    }

    // Local changes travel along when the switch can carry them: a staged edit with an unstaged one over it, a staged new
    // file, an untracked file and an ignored one, on files both branches hold alike, are all the same after the switch, in
    // the index and in the working tree.
    [Fact]
    public async Task WhenSwitchingWithChangesThatTravel_ShouldKeepEveryChangeIncludingStagedAndUntracked()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var work = await Clone(fixture); var ct = fixture.Token; var runner = fixture.Runner;
        var reader = new CliRepositoryReader(runner); var writer = new GitWriteRouter(runner);
        await fixture.GitAtAsync(work, "branch", "topic", "refs/remotes/origin/topic");
        await File.WriteAllTextAsync(Path.Combine(work, ".git", "info", "exclude"), "*.log\n", ct);
        await File.WriteAllTextAsync(Path.Combine(work, "readme.txt"), "shared\nstaged\n", ct);
        await File.WriteAllTextAsync(Path.Combine(work, "added.txt"), "added\n", ct);
        await fixture.GitAtAsync(work, "add", "readme.txt", "added.txt");
        await File.WriteAllTextAsync(Path.Combine(work, "readme.txt"), "shared\nstaged\nunstaged\n", ct);
        await File.WriteAllTextAsync(Path.Combine(work, "loose.txt"), "loose\n", ct);
        await File.WriteAllTextAsync(Path.Combine(work, "build.log"), "log\n", ct);
        var before = await ChangesAsync(fixture, work);

        var switched = await writer.ExecuteAsync(new BranchRequest(await reader.ReadAsync(work, ct), new("topic"), BranchAction.Switch), ct);

        Assert.True(switched.Outcome == GitWriteOutcome.Succeeded, switched.Message + " " + switched.Details);
        Assert.Equal("topic", (await reader.ReadAsync(work, ct)).Branch);
        Assert.Equal(before, await ChangesAsync(fixture, work));
    }

    // A branch started at a remote branch, not set to track one, takes an upstream only as the user's branch.autoSetupMerge
    // says, as git switch -c does in a terminal: by default and under always the remote branch, under false none, and under
    // inherit none either, since a remote branch has no upstream to inherit; Git only warns then, and Trag says nothing.
    [Theory]
    [InlineData(null, "origin/topic")]
    [InlineData("false", null)]
    [InlineData("always", "origin/topic")]
    [InlineData("inherit", null)]
    public async Task WhenABranchIsCreatedFromARemoteStartPoint_ShouldFollowAutoSetupMerge(string? setting, string? upstream)
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var work = await Clone(fixture); var ct = fixture.Token; var runner = fixture.Runner;
        var reader = new CliRepositoryReader(runner); var writer = new GitWriteRouter(runner);
        if (setting is not null) await fixture.GitAtAsync(work, "config", "branch.autoSetupMerge", setting);

        var created = await writer.ExecuteAsync(new BranchRequest(await reader.ReadAsync(work, ct), new("feature", "refs/remotes/origin/topic"), BranchAction.Create), ct);

        Assert.True(created.Outcome == GitWriteOutcome.Succeeded, created.Message + " " + created.Details);
        var tracked = await runner.RunAsync(work, ["rev-parse", "--abbrev-ref", "feature@{upstream}"], ct);
        Assert.Equal(upstream, tracked.ExitCode == 0 ? System.Text.Encoding.UTF8.GetString(tracked.StandardOutput).Trim() : null);
    }

    // A submodule is Trag's to update only when asked: a switch under the user's submodule.recurse leaves the submodule on
    // its own branch with its files, as a merge does, and the submodule shows as changed instead.
    [Fact]
    public async Task WhenSubmoduleRecurseIsOnInTheUsersConfig_ShouldSwitchWithoutTouchingSubmodules()
    {
        using var f = await SubmoduleFixture.CreateAsync();
        var sub = Path.Combine(f.Root, "sub");
        await f.GitAsync("switch", "-q", "-c", "next");
        await File.WriteAllTextAsync(Path.Combine(sub, "s.txt"), "s\n", f.Token);
        await f.GitAtAsync(sub, "add", "s.txt");
        await f.GitAtAsync(sub, "commit", "-m", "Sub two");
        await f.GitAsync("add", "sub");
        await f.GitAsync("commit", "-m", "Sub two");
        await f.GitAsync("config", "submodule.recurse", "true");

        var switched = await new GitWriteRouter(f.Runner).ExecuteAsync(new BranchRequest(await f.ReadAsync(), new("main"), BranchAction.Switch), f.Token);

        Assert.Equal((GitWriteOutcome.Succeeded, Trag.Domain.Presentation.Text.BranchOutcomeText.Switched("main")), (switched.Outcome, switched.Message));
        Assert.Equal("refs/heads/main", (await f.GitAtAsync(sub, "symbolic-ref", "HEAD")).Trim());
        Assert.True(File.Exists(Path.Combine(sub, "s.txt")));
    }

    // Git LFS set up as Git for Windows sets it, on a remote Trag does not synchronize with: a branch of it without Git LFS
    // files has nothing to download, so switching to it and merging it need nothing from the remote and are not refused.
    [Theory]
    [InlineData("two push URLs", "switch")]
    [InlineData("mirror", "switch")]
    [InlineData("ssh.variant", "switch")]
    [InlineData("two push URLs", "merge")]
    public async Task WhenABranchWithoutLfsFilesIsOnARemoteTragDoesNotSync_ShouldStillSwitchToItOrMergeIt(string limitation, string move)
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var work = await Clone(fixture); var ct = fixture.Token; var runner = fixture.Runner;
        await LfsConfig.SetAsync(fixture, work, installed: true);
        string[][] settings = limitation switch
        {
            "two push URLs" => [["--add", "remote.origin.pushurl", fixture.Root], ["--add", "remote.origin.pushurl", fixture.Container]],
            "mirror" => [["remote.origin.mirror", "true"]],
            _ => [["ssh.variant", "weird"]]
        };
        foreach (var setting in settings) await fixture.GitAtAsync(work, ["config", .. setting]);
        var reader = new CliRepositoryReader(runner); var sync = new GitSyncFacade(runner, reader);
        Assert.NotEqual("", (await sync.ReadAsync(await reader.ReadAsync(work, ct), "origin", null, ct)).Remote.Limitation);
        await using var workspace = new RepositoryWorkspace(reader, writer: new GitWriteRouter(runner, sync, reader), branchReader: new CliBranchReader(runner));
        var repository = new Repository(new RepositoryServices(workspace, new StatusBarState(), new ImmediateViewDispatcher()));
        repository.Apply((await workspace.OpenAsync(work, ct))!);
        await repository.Branches.List.Completion;
        var topic = repository.Branches.List.Value.Remote.Single(branch => branch.Name == "origin/topic");

        if (move == "switch")
        {
            var switched = await repository.Branches.SwitchToAsync(topic);
            Assert.Equal((GitWriteOutcome.Succeeded, BranchOutcomeText.CreatedAndOn("topic")), (switched!.Outcome, switched.Message));
        }
        else
        {
            var choice = new MergeChoice(topic, repository.Branches.List.Value.Current, "main");
            repository.Merge.ReadPreview(choice);
            await repository.Merge.Preview.Completion;
            var merged = await repository.Merge.StartAsync(choice, repository.Merge.Preview.Value);
            Assert.True(merged?.Outcome == GitWriteOutcome.Succeeded, merged?.Message + " " + merged?.Details);
        }
        Assert.Equal(topic.Head, await fixture.GitAtAsync(work, "rev-parse", "HEAD"));
    }

    // The upstream has main, topic with a commit of its own, and release at main; work is a clone of it, on main.
    // Answers the folder of the clone.
    private static async Task<string> Clone(GitFixture fixture)
    {
        var upstream = fixture.Root; var work = Path.Combine(fixture.Container, "work");
        await fixture.WriteAsync("readme.txt", "shared\n");
        await fixture.CommitAsync("Initial", "readme.txt");
        await fixture.GitAtAsync(upstream, "switch", "-c", "topic");
        await fixture.WriteAsync("topic.txt", "topic\n");
        await fixture.CommitAsync("Topic only", "topic.txt");
        await fixture.GitAtAsync(upstream, "branch", "release", "main");
        await fixture.GitAtAsync(upstream, "switch", "main");
        await fixture.GitAtAsync(fixture.Container, "clone", "--", upstream, work);
        return work;
    }

    // What a switch has to carry: the unstaged and the staged changes, and the untracked and ignored files with their text.
    private static async Task<string[]> ChangesAsync(GitFixture fixture, string work) =>
    [
        await fixture.GitAtAsync(work, "diff"), await fixture.GitAtAsync(work, "diff", "--cached"),
        await fixture.GitAtAsync(work, "ls-files", "--others", "--exclude-standard"),
        await fixture.GitAtAsync(work, "ls-files", "--others", "--ignored", "--exclude-standard"),
        await File.ReadAllTextAsync(Path.Combine(work, "loose.txt"), fixture.Token), await File.ReadAllTextAsync(Path.Combine(work, "build.log"), fixture.Token)
    ];

    // The writes of the creation test without its checks: feature/x at origin/topic, then the four refused requests
    // over the same snapshot.
    private static async Task CreateFeature(GitFixture fixture, CliRepositoryReader reader, GitWriteRouter writer, string work)
    {
        var ct = fixture.Token;
        var snapshot = await reader.ReadAsync(work, ct);
        await writer.ExecuteAsync(new BranchRequest(snapshot, new("feature/x", "refs/remotes/origin/topic", Switch: false), BranchAction.Create), ct);
        await writer.ExecuteAsync(new BranchRequest(snapshot, new("bad..name"), BranchAction.Create), ct);
        await writer.ExecuteAsync(new BranchRequest(snapshot, new("feature/x"), BranchAction.Create), ct);
        await writer.ExecuteAsync(new BranchRequest(snapshot, new("nope"), BranchAction.Switch), ct);
        await writer.ExecuteAsync(new BranchRequest(snapshot, new("feature/y", "refs/heads/missing"), BranchAction.Create), ct);
    }

    // The writes of the switch test without its checks: the switch to feature/x with readme.txt edited, the refused switch
    // back to main with topic.txt edited, and then the edit to topic.txt undone.
    private static async Task SwitchWithLocalChanges(GitFixture fixture, CliRepositoryReader reader, GitWriteRouter writer, string work)
    {
        var ct = fixture.Token;
        await File.WriteAllTextAsync(Path.Combine(work, "readme.txt"), "shared\nlocal edit\n", ct);
        var snapshot = await reader.ReadAsync(work, ct);
        await writer.ExecuteAsync(new BranchRequest(snapshot, new("feature/x"), BranchAction.Switch), ct);
        await File.WriteAllTextAsync(Path.Combine(work, "topic.txt"), "edited\n", ct);
        snapshot = await reader.ReadAsync(work, ct);
        await writer.ExecuteAsync(new BranchRequest(snapshot, new("main"), BranchAction.Switch), ct);
        await fixture.GitAtAsync(work, "checkout", "--", "topic.txt");
    }

    // The write of the tracking test without its checks: release created from origin/release and switched to.
    private static async Task TrackRelease(GitFixture fixture, CliRepositoryReader reader, GitWriteRouter writer, string work)
    {
        var snapshot = await reader.ReadAsync(work, fixture.Token);
        await writer.ExecuteAsync(new BranchRequest(snapshot, new("release", null, true, "refs/remotes/origin/release"), BranchAction.Create), fixture.Token);
    }
}
