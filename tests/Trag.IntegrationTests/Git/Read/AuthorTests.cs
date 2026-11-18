using System.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// The authors the co-author picker offers come from the newest commits as .mailmap names them, read as UTF-8 whatever
// the repository's log output encoding is.
public sealed class AuthorTests
{
    [Fact]
    public async Task WhenAuthorsAreRead_ShouldGiveEachCommitsAuthorNewestFirstAsMailmapNamesThem()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.GitAsync("config", "i18n.logOutputEncoding", "ISO-8859-1");
        await f.WriteAsync(".mailmap", "Ana Anić <ana@example.invalid> <ana.old@example.invalid>\n");
        await f.CommitAsync("mailmap", ".mailmap");
        await f.GitAsync("commit", "--allow-empty", "-m", "old", "--author=Ana <ana.old@example.invalid>");
        await f.GitAsync("commit", "--allow-empty", "-m", "jose", "--author=José M. <jose@example.invalid>");
        var me = (await f.ReadAsync()).Author!;

        var authors = await new CliHistoryReader(f.Runner).ReadAuthorsAsync(f.Root, f.Token);

        Assert.Equal([new GitIdentity("José M.", "jose@example.invalid"), new GitIdentity("Ana Anić", "ana@example.invalid"), me], authors);
    }

    // A long history is read only as far as its newest 2,000 commits.
    [Fact]
    public async Task WhenTheHistoryIsLongerThanTheLimit_ShouldReadOnlyTheNewestCommitsAuthors()
    {
        using var f = await GitFixture.CreateAsync("main");
        var input = new StringBuilder();
        for (var i = 0; i < 2001; i++)
        {
            var identity = i == 0 ? "Oldest <oldest@example.invalid>" : "Fixture <fixture@example.invalid>";
            input.Append($"commit refs/heads/main\ncommitter {identity} {1700000000 + i} +0000\ndata 2\nc\n\n");
        }
        input.Append("done\n");
        Assert.Equal(0, (await new GitProcessRunner().RunAsync(f.Root, ["fast-import", "--quiet"], f.Token, standardInput: Encoding.UTF8.GetBytes(input.ToString()))).ExitCode);

        var authors = await new CliHistoryReader(f.Runner).ReadAuthorsAsync(f.Root, f.Token);

        Assert.Equal(2000, authors.Count);
        Assert.DoesNotContain(new GitIdentity("Oldest", "oldest@example.invalid"), authors);
    }
}
