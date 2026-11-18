using Trag.Domain.Diff;
using Trag.TestSupport;

namespace Trag.IntegrationTests;

// The shared content corpus as Git sees it, so a sample one byte off cannot quietly turn the text-with-NUL row of a
// feature test into a second binary one: for each sample, the added lines diff --numstat counts ("-" for binary), the
// index's line endings as ls-files --eol classes them, and the size Git stored. Git does not look at encodings, so the
// byte order mark, Latin-2 and invalid UTF-8 samples are held by their size alone. The fixture turns core.autocrlf off,
// so Git stores the bytes as written under the suite's global configuration and the hostile pass's (autocrlf=true,
// eol=crlf).
public sealed class TestContentsTests
{
    public static TheoryData<string> Samples => [.. TestContents.Hostile];

    private static readonly Dictionary<string, (string Added, string Eol, int Size)> Seen = new()
    {
        ["empty"] = ("0", "none", 0),
        ["one byte without a newline"] = ("1", "none", 1),
        ["only a newline"] = ("1", "lf", 1),
        ["CRLF"] = ("2", "crlf", 15),
        ["LF"] = ("2", "lf", 13),
        ["mixed CRLF and LF"] = ("3", "mixed", 21),
        // Git's line-ending stats count a lone CR as binary; its diff splits lines at LF only, so the file is one line.
        ["CR only"] = ("1", "-text", 19),
        ["UTF-8 with a BOM"] = ("2", "lf", 16),
        ["UTF-16 LE with a BOM"] = ("-", "-text", 32),
        ["Latin-2"] = ("2", "lf", 17),
        ["invalid UTF-8 mid-line"] = ("1", "lf", 19),
        // The diff looks for a NUL in the first 8 000 bytes only; the line-ending stats read the whole file.
        ["NUL as the 8 001st byte"] = ("101", "-text", 8_018),
        ["a line of 100 000 characters"] = ("1", "lf", 100_001),
        ["one line over the diff line limit"] = ($"{DiffLimits.LineLimit + 1}", "lf", 288_896),
        ["one byte over the diff byte limit"] = ("40961", "lf", DiffLimits.ByteLimit + 1),
    };

    [Theory, MemberData(nameof(Samples))]
    public async Task WhenASampleIsStaged_ShouldGiveGitItsExpectedLineCountLineEndingsAndSize(string sample)
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.GitAsync("config", "core.autocrlf", "false");
        await File.WriteAllBytesAsync(Path.Combine(fixture.Root, "sample"), TestContents.Bytes(sample), fixture.Token);
        await fixture.GitAsync("add", "--", "sample");
        var added = (await fixture.GitAsync("diff", "--cached", "--numstat", "--", "sample")).Split('\t')[0];
        var eol = (await fixture.GitAsync("ls-files", "--eol", "--", "sample")).Split(' ')[0]["i/".Length..];
        var size = int.Parse(await fixture.GitAsync("cat-file", "-s", ":sample"));
        Assert.Equal(Seen[sample], (added, eol, size));
    }
}
