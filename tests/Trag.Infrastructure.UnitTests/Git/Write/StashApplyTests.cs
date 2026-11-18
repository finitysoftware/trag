using Trag.Domain.Merge;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Write;

// Applying a stash against real Git, and what the outcome says about the working tree Git left.
public sealed class StashApplyTests
{
    // Git puts back the tracked changes and every untracked file of the stash it can before it meets one already in the
    // working tree; that file stays as it is, Git exits 1, and the entry stays in the list.
    [Fact]
    public async Task WhenAnIncludeUntrackedStashIsAppliedAndAnUntrackedFileIsInTheWay_ShouldSayWhatAlreadyLanded()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "core.autocrlf", "false");
        await fixture.WriteAsync("a.txt", "one\n");
        await fixture.CommitAsync("first");
        await fixture.WriteAsync("a.txt", "changed\n");
        await fixture.WriteAsync("in-the-way.txt", "stashed\n");
        await fixture.WriteAsync("free.txt", "stashed\n");
        await fixture.GitAsync("stash", "push", "--include-untracked", "-m", "with untracked files");
        await fixture.WriteAsync("in-the-way.txt", "local\n");

        var result = await ApplyAsync(fixture);

        Assert.Equal((GitWriteOutcome.Rejected, "Git applied the stash except the untracked files named below, which already exist in the working tree and stayed as they are. The stash entry is kept."),
            (result.Outcome, result.Message));
        Assert.Contains("in-the-way.txt already exists", result.Details);
        Assert.Equal(("changed\n", "stashed\n", "local\n"), (Read(fixture, "a.txt"), Read(fixture, "free.txt"), Read(fixture, "in-the-way.txt")));
        Assert.Equal("On fixture: with untracked files", await fixture.GitAsync("stash", "list", "--format=%gs"));
    }

    // A file the stash added to the index is one Git does not write over an untracked file of the same name: it applies
    // nothing and names the file. Committing it or a stash without untracked files, as a local change would need, does
    // not move it.
    [Fact]
    public async Task WhenAFileTheStashAddedIsUntrackedInTheWorkingTree_ShouldSayToMoveItAndApplyNothing()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "core.autocrlf", "false");
        await fixture.WriteAsync("a.txt", "one\n");
        await fixture.CommitAsync("first");
        await fixture.WriteAsync("a.txt", "changed\n");
        await fixture.WriteAsync("new.txt", "stashed\n");
        await fixture.GitAsync("add", "new.txt");
        await fixture.GitAsync("stash", "push", "-m", "with a new file");
        await fixture.WriteAsync("new.txt", "local\n");

        var result = await ApplyAsync(fixture);

        Assert.Equal((GitWriteOutcome.Rejected, "Git kept the working tree: untracked files listed below would be overwritten by files from the stash. Move or remove them, then apply again."),
            (result.Outcome, result.Message));
        Assert.Equal(("one\n", "local\n"), (Read(fixture, "a.txt"), Read(fixture, "new.txt")));
        Assert.Equal("On fixture: with a new file", await fixture.GitAsync("stash", "list", "--format=%gs"));
    }

    // Apply runs without --index: a staged change comes back unstaged, and only a new file stays staged, as Git keeps it.
    [Fact]
    public async Task WhenAStashHoldsStagedAndUnstagedChangesOfOneFile_ShouldSayTheStagedPartComesBackUnstaged()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "core.autocrlf", "false");
        await fixture.WriteAsync("a.txt", "one\n");
        await fixture.CommitAsync("first");
        await fixture.WriteAsync("a.txt", "staged\n");
        await fixture.WriteAsync("new.txt", "new\n");
        await fixture.GitAsync("add", "a.txt", "new.txt");
        await fixture.WriteAsync("a.txt", "staged\nunstaged\n");
        await fixture.GitAsync("stash", "push", "-m", "both");

        var result = await ApplyAsync(fixture);

        Assert.Equal((GitWriteOutcome.Succeeded, "Applied stash@{0}. What was staged is unstaged now, except new files. The entry stays in the stash list until you drop it."),
            (result.Outcome, result.Message));
        Assert.Equal("staged\nunstaged\n", Read(fixture, "a.txt"));
        Assert.Equal("new.txt", await fixture.GitAsync("diff", "--cached", "--name-only"));
    }

    // On a branch without a commit, every file the stash changed is one HEAD does not have: Git applies the stash as a
    // modify/delete conflict, which the stash's side resolves like any other stash conflict.
    [Fact]
    public async Task WhenApplyingOnAnUnbornHead_ShouldLeaveAConflictTheStashsSideResolves()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "core.autocrlf", "false");
        await fixture.WriteAsync("a.txt", "one\n");
        await fixture.CommitAsync("first");
        await fixture.WriteAsync("a.txt", "stashed\n");
        await fixture.GitAsync("stash", "push", "-m", "before the orphan");
        await fixture.GitAsync("switch", "--orphan", "empty");
        var router = new GitWriteRouter(fixture.Runner);
        var id = await fixture.GitAsync("rev-parse", "stash@{0}");

        var applied = await router.ExecuteAsync(new StashRequest(await fixture.ReadAsync(), new(Id: id), StashAction.Apply), fixture.Token);

        Assert.True(applied.Outcome == GitWriteOutcome.NeedsResolution, applied.Message + applied.Details);
        var conflicted = await fixture.ReadAsync();
        var conflict = Assert.Single(conflicted.Conflicts);
        Assert.Equal("a.txt", conflict.Path.Display);
        var resolved = await router.ExecuteAsync(new ConflictSideRequest(conflicted, conflict.Path, ConflictChoice.Theirs), fixture.Token);
        Assert.True(resolved.Outcome == GitWriteOutcome.Succeeded, resolved.Message + resolved.Details);
        var after = await fixture.ReadAsync();
        Assert.Equal((RepositoryOperation.None, "stashed\n"), (after.Operation, Read(fixture, "a.txt")));
        Assert.Equal("a.txt", await fixture.GitAsync("diff", "--cached", "--name-only"));
    }

    private static async Task<GitWriteResult> ApplyAsync(GitFixture fixture)
    {
        var git = new WriteCommandRunner(new GitProcessRunner());
        var snapshot = await fixture.ReadAsync();
        var id = await fixture.GitAsync("rev-parse", "stash@{0}");
        return await new StashWriter(git, new StashEntryResolver(git)).StashAsync(new StashRequest(snapshot, new(Id: id), StashAction.Apply), snapshot, fixture.Token);
    }

    private static string Read(GitFixture fixture, string path) => File.ReadAllText(Path.Combine(fixture.Root, path));
}
