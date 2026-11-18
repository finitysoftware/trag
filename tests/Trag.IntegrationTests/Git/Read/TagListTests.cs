using Trag.Domain.Naming;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

public sealed class TagListTests
{
    // Highest version first; an annotated tag names the commit it points at and its own subject, a lightweight one the
    // commit's subject, and a tag of something other than a commit is left out.
    [Fact]
    public async Task WhenTagsAreRead_ShouldListTheTagsOfCommitsHighestVersionFirstWithTheirCommitsAndSubjects()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await fixture.WriteAsync("a.txt", "1\n");
        var first = await fixture.CommitAsync("First commit", "a.txt");
        await fixture.WriteAsync("a.txt", "2\n");
        var second = await fixture.CommitAsync("Second commit", "a.txt");
        await fixture.GitAsync("tag", "light", first);
        await fixture.GitAsync("tag", "v1.9", first);
        await fixture.GitAsync("tag", "--annotate", "--message=Release ten\n\nNotes.", "v1.10", second);
        await fixture.GitAsync("tag", "tree", await fixture.GitAsync("rev-parse", "HEAD^{tree}"));

        var tags = await new CliBranchReader(fixture.Runner).ReadTagsAsync(fixture.Root, fixture.Token);

        Assert.Equal([("v1.10", second, "Release ten"), ("v1.9", first, "First commit"), ("light", first, "First commit")],
            tags.Select(tag => (tag.Name, tag.Commit, tag.Subject)));
    }

    // A tag of a tag names the commit at the end of the chain, with its own subject, whatever Git's version, and so does
    // a second tag of the same tag; a chain that ends at a tree is left out like a tag of a tree.
    [Fact]
    public async Task WhenATagPointsAtAnotherTag_ShouldPeelToTheCommit()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var commit = await fixture.CommitAsync("Only commit");
        await fixture.GitAsync("tag", "--annotate", "--message=Inner", "inner", commit);
        await fixture.GitAsync("tag", "--annotate", "--message=Outer", "outer", "inner");
        await fixture.GitAsync("tag", "--annotate", "--message=Also outer", "also-outer", "inner");
        await fixture.GitAsync("tag", "--annotate", "--message=Tree", "tree", await fixture.GitAsync("rev-parse", "HEAD^{tree}"));
        await fixture.GitAsync("tag", "--annotate", "--message=Of a tree", "of-tree", "tree");

        var tags = await new CliBranchReader(fixture.Runner).ReadTagsAsync(fixture.Root, fixture.Token);

        Assert.Equal([("outer", commit, "Outer"), ("inner", commit, "Inner"), ("also-outer", commit, "Also outer")],
            tags.Select(tag => (tag.Name, tag.Commit, tag.Subject)));
    }

    // More tags than the list holds: the highest versions are listed, up to the limit. The tags go into Git's packed-refs
    // file, as a clone leaves them; thousands of loose ref files written and read at once can take Windows longer than
    // the reader's 30 seconds to open the first time.
    [Fact]
    public async Task WhenThereAreMoreTagsThanTheLimit_ShouldListTheHighestVersionsUpToIt()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var commit = await fixture.CommitAsync("Only commit");
        var packed = string.Concat(Enumerable.Range(0, GitLimits.BranchCount + 1).Select(i => commit + " refs/tags/v" + i + "\n"));
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, ".git", "packed-refs"), packed, fixture.Token);

        var tags = await new CliBranchReader(fixture.Runner).ReadTagsAsync(fixture.Root, fixture.Token);

        Assert.Equal(GitLimits.BranchCount, tags.Count);
        Assert.Equal(("v" + GitLimits.BranchCount, "v1"), (tags[0].Name, tags[^1].Name));
    }

    [Fact]
    public async Task WhenARepositoryHasNoTags_ShouldListNone()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await fixture.CommitAsync("Only commit");

        Assert.Empty(await new CliBranchReader(fixture.Runner).ReadTagsAsync(fixture.Root, fixture.Token));
    }
}
