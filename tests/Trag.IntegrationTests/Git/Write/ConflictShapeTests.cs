using Trag.Domain.Merge;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

// Conflicts that are not two edits of one text file, each as Git 2.55 leaves it: a file one side renamed and the other
// deleted (UD under the new name), a folder on one side where the other has a file (Git moves the file to p~HEAD, AU), a
// link both sides pointed elsewhere (UU, 120000, core.symlinks=false), a populated submodule one side deleted (UD,
// 160000), and a file on one side where the other added a submodule (UA for the submodule, the file at p~HEAD, AU). A side
// is taken for the whole file or the conflict is marked resolved and the merge finished, or the merge is aborted.
public sealed class ConflictShapeTests
{
    private static readonly string[] Shapes = ["rename-delete", "dir-file", "symlink", "submodule-deleted", "file-submodule"];

    public static TheoryData<string> AllShapes() => [.. Shapes];

    public static TheoryData<string, ConflictChoice> ShapesAndSides()
    {
        var rows = new TheoryData<string, ConflictChoice>();
        foreach (var shape in Shapes)
            foreach (var side in new[] { ConflictChoice.Mine, ConflictChoice.Theirs }) rows.Add(shape, side);
        return rows;
    }

    // The merge commit holds that side's entry at the conflict's path, or none where that side has none, and the working
    // tree matches it. Any other conflict the shape leaves (p~HEAD beside the submodule) keeps our file.
    [Theory]
    [MemberData(nameof(ShapesAndSides))]
    public async Task WhenAConflictOfAnotherShapeIsResolvedWithASide_ShouldFinishWithThatSidesEntry(string shape, ConflictChoice side)
    {
        using var f = await ConflictedAsync(shape);
        var path = PathOf(shape);
        var entries = (await f.GitAsync("ls-files", "--stage", "--", path)).Split('\n')
            .Select(line => line.Split('\t')[0].Split(' ')).ToDictionary(entry => entry[2], entry => entry[0] + " " + entry[1]);
        var expected = entries.GetValueOrDefault(side == ConflictChoice.Mine ? "2" : "3");

        var used = await WriteAsync(f, new ConflictSideRequest(await f.ReadAsync(), Snapshots.Path(path), side));
        foreach (var other in (await f.ReadAsync()).Conflicts.Select(file => file.Path).ToArray())
            await WriteAsync(f, new ConflictSideRequest(await f.ReadAsync(), other, ConflictChoice.Mine));
        var finished = await WriteAsync(f, new CommitRequest(await f.ReadAsync(), "Merge branch 'feature'", FinishesMerge: true));

        Assert.True(used.Outcome == GitWriteOutcome.Succeeded, used.Message + used.Details);
        Assert.True(finished.Outcome == GitWriteOutcome.Succeeded, finished.Message + finished.Details);
        var tree = await f.GitAsync("ls-tree", "HEAD", "--", path);
        Assert.Equal(expected, tree.Length == 0 ? null : tree.Split('\t')[0].Replace(" blob ", " ").Replace(" commit ", " "));
        Assert.DoesNotContain((await f.ReadAsync()).Files, file => file.Path.Display == path);
    }

    // Git leaves our side in the working tree, so marking the conflict resolved commits our entry, a link's mode included.
    [Theory]
    [InlineData("rename-delete")]
    [InlineData("dir-file")]
    [InlineData("symlink")]
    public async Task WhenAConflictOfAnotherShapeIsMarkedResolved_ShouldFinishWithWhatTheWorkingTreeHolds(string shape)
    {
        using var f = await ConflictedAsync(shape);
        var path = PathOf(shape);
        var ours = (await f.GitAsync("ls-files", "--stage", "--", path)).Split('\n')
            .Select(line => line.Split('\t')[0].Split(' ')).Single(entry => entry[2] == "2");

        var marked = await WriteAsync(f, new StageRequest(await f.ReadAsync(), [Snapshots.Path(path)], Resolution: true));
        var finished = await WriteAsync(f, new CommitRequest(await f.ReadAsync(), "Merge branch 'feature'", FinishesMerge: true));

        Assert.True(marked.Outcome == GitWriteOutcome.Succeeded, marked.Message + marked.Details);
        Assert.True(finished.Outcome == GitWriteOutcome.Succeeded, finished.Message + finished.Details);
        Assert.StartsWith(ours[0] + " blob " + ours[1] + "\t", await f.GitAsync("ls-tree", "HEAD", "--", path));
        Assert.DoesNotContain((await f.ReadAsync()).Files, file => file.Path.Display == path);
    }

    // A submodule conflict is marked resolved only by a side: the deleted submodule kept by marking it is no longer in
    // .gitmodules, and the submodule the other side added has no commit checked out for git add to stage. The conflict stays.
    [Theory]
    [InlineData("submodule-deleted", IndexOutcomeText.SubmodulesNotStaged)]
    [InlineData("file-submodule", IndexOutcomeText.NotUpdated)]
    public async Task WhenASubmoduleConflictIsMarkedResolved_ShouldRefuseAndKeepTheConflict(string shape, string refusal)
    {
        using var f = await ConflictedAsync(shape);
        var path = PathOf(shape);
        var before = await f.GitAsync("ls-files", "--stage");

        var marked = await WriteAsync(f, new StageRequest(await f.ReadAsync(), [Snapshots.Path(path)], Resolution: true));

        Assert.Equal((GitWriteOutcome.Rejected, refusal), (marked.Outcome, marked.Message));
        Assert.Equal(before, await f.GitAsync("ls-files", "--stage"));
        Assert.Contains((await f.ReadAsync()).Conflicts, file => file.Path.Display == path);
    }

    [Theory]
    [MemberData(nameof(AllShapes))]
    public async Task WhenAMergeStoppedOnAConflictOfAnotherShapeIsAborted_ShouldRestoreTheBranchAndItsIndex(string shape)
    {
        using var f = await ConflictedAsync(shape);
        var before = await f.ReadAsync();

        var aborted = await WriteAsync(f, new MergeAbortRequest(before));

        Assert.True(aborted.Outcome == GitWriteOutcome.Succeeded, aborted.Message + aborted.Details);
        var after = await f.ReadAsync();
        Assert.Equal((RepositoryOperation.None, before.Head), (after.Operation, after.Head));
        Assert.Equal("", await f.GitAsync("diff", "--cached", "--name-only"));
        Assert.DoesNotContain(after.Files, file => file.IsConflict || file.IsStaged);
    }

    // git rm keeps a submodule whose working tree has changes of its own; the side is not taken and the conflict stays.
    [Fact]
    public async Task WhenASubmoduleWithLocalChangesIsDeletedAsASide_ShouldRefuseAndKeepItsChanges()
    {
        using var f = await ConflictedAsync("submodule-deleted");
        await File.WriteAllTextAsync(Path.Combine(f.Root, "sub", "local.txt"), "local work\n", f.Token);
        await f.GitAtAsync(Path.Combine(f.Root, "sub"), "add", "local.txt");

        var result = await WriteAsync(f, new ConflictSideRequest(await f.ReadAsync(), Snapshots.Path("sub"), ConflictChoice.Theirs));

        Assert.Equal((GitWriteOutcome.Rejected, ConflictOutcomeText.SideNotTaken), (result.Outcome, result.Message));
        Assert.Contains("local modifications", result.Details);
        Assert.Equal("local work\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "sub", "local.txt"), f.Token));
        Assert.Contains((await f.ReadAsync()).Conflicts, file => file.Path.Display == "sub");
    }

    private static string PathOf(string shape) => shape switch
    {
        "rename-delete" => "b.txt",
        "dir-file" => "p~HEAD",
        "symlink" => "link",
        "file-submodule" => "p",
        _ => "sub",
    };

    // main merges feature and stops on the shape's conflict.
    private static async Task<GitFixture> ConflictedAsync(string shape)
    {
        var f = shape.Contains("submodule", StringComparison.Ordinal) ? await SubmoduleFixture.CreateAsync() : await GitFixture.CreateAsync("main");
        try
        {
            await f.GitAsync("config", "core.autocrlf", "false");
            switch (shape)
            {
                case "rename-delete":
                    await f.WriteAsync("a.txt", "one\ntwo\n"); await f.CommitAsync("Base");
                    await f.GitAsync("switch", "-c", "feature"); await f.GitAsync("rm", "-q", "a.txt"); await f.GitAsync("commit", "-m", "Delete");
                    await f.GitAsync("switch", "main"); await f.GitAsync("mv", "a.txt", "b.txt"); await f.GitAsync("commit", "-m", "Rename");
                    break;
                case "dir-file":
                    await f.WriteAsync("base.txt", "base\n"); await f.CommitAsync("Base");
                    await f.GitAsync("switch", "-c", "feature"); await f.WriteAsync("p/x.txt", "in a folder\n"); await f.CommitAsync("Folder");
                    await f.GitAsync("switch", "main"); await f.WriteAsync("p", "a file\n"); await f.CommitAsync("File");
                    break;
                case "symlink":
                    await f.GitAsync("config", "core.symlinks", "false");
                    await f.WriteAsync("base.txt", "base\n"); await f.CommitAsync("Base");
                    foreach (var (branch, target) in new[] { ("main", "t0"), ("feature", "t-feature"), ("main", "t-main") })
                    {
                        if (branch == "feature") await f.GitAsync("switch", "-c", "feature");
                        else if (target != "t0") await f.GitAsync("switch", "main");
                        await f.WriteAsync("link", target);
                        await f.GitAsync("update-index", "--add", "--cacheinfo", "120000," + await f.GitAsync("hash-object", "-w", "link") + ",link");
                        await f.GitAsync("commit", "-m", target);
                    }
                    break;
                case "file-submodule":
                    await f.GitAsync("switch", "-c", "feature");
                    await f.GitAsync("-c", "protocol.file.allow=always", "submodule", "add", "--", new Uri(SubmoduleFixture.Origin(f)).AbsoluteUri, "p");
                    await f.GitAsync("commit", "-m", "Add p");
                    await f.GitAsync("switch", "main");
                    Directory.Delete(Path.Combine(f.Root, "p"), recursive: true);
                    await f.WriteAsync("p", "a file\n"); await f.CommitAsync("File", "p");
                    break;
                default:
                    var sub = Path.Combine(f.Root, "sub");
                    await f.GitAsync("switch", "-c", "feature"); await f.GitAsync("rm", "-q", "sub"); await f.GitAsync("commit", "-m", "Delete sub");
                    await f.GitAsync("switch", "main");
                    await f.GitAsync("-c", "protocol.file.allow=always", "submodule", "update", "--init");
                    await f.GitAtAsync(sub, "commit", "--allow-empty", "-m", "Sub main"); await f.GitAsync("add", "sub"); await f.GitAsync("commit", "-m", "Main moves sub");
                    break;
            }
            Assert.NotEqual(0, (await f.RunAsync("merge", "--no-ff", "--no-edit", "feature")).ExitCode);
            Assert.Single((await f.ReadAsync()).Conflicts, file => file.Path.Display == PathOf(shape));
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    private static Task<GitWriteResult> WriteAsync(GitFixture f, GitWriteRequest request) => new GitWriteRouter(f.Runner).ExecuteAsync(request, f.Token);
}
