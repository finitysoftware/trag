using System.Text;
using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.UnitTests.Git.Parsing;

// git cat-file commit as Git 2.55 prints it: the tree, a line for each parent, then the author and the committer.
public sealed class CommitObjectTests
{
    private const string Tree = "tree 4b825dc642cb6eb9a060e54bf8d69288fbee4904\n";
    private const string Rest = "author Ana <ana@example.com> 1793434000 +0100\ncommitter Ana <ana@example.com> 1793434000 +0100\n\nsecond\n";

    [Theory]
    [InlineData(Tree + "parent bf50015ec4634c3f7cbf8bf5f581e2dfff3cc075\n" + Rest, true)]
    [InlineData(Tree + Rest, false)]
    public void WhenACommitIsRead_ShouldSayWhetherItNamesAParent(string commit, bool namesParent) =>
        Assert.Equal(namesParent, CommitObject.NamesParent(Encoding.UTF8.GetBytes(commit)));
}
