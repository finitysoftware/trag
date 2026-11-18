using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.UnitTests.Git.Processes;

public sealed class GitRecordStreamTests
{
    // A field longer than the stream's buffer, then a short one.
    private static byte[] LongFieldThenNext() => Enumerable.Repeat((byte)'x', 20_000).Append((byte)0).Concat("next\0"u8.ToArray()).ToArray();

    // The stream after reading the long field.
    private static async Task<GitRecordStream> AfterTheLongFieldAsync()
    {
        var records = new GitRecordStream(new MemoryStream(LongFieldThenNext()));
        await records.ReadAsync(default);
        return records;
    }

    // ...and after reading the short one too.
    private static async Task<GitRecordStream> AfterBothFieldsAsync()
    {
        var records = await AfterTheLongFieldAsync();
        await records.ReadAsync(default);
        return records;
    }

    // Two six-field commit records, each longer than the stream's buffer.
    private static GitRecordStream TwoCommits(byte[] whole) => new(new MemoryStream(whole.Concat(whole).ToArray()));

    [Fact]
    public async Task WhenAFieldSpansBufferRefills_ShouldReadItExactly()
    {
        var records = new GitRecordStream(new MemoryStream(LongFieldThenNext()));
        Assert.Equal(20_000, (await records.ReadAsync(default))!.Value.Length);
    }

    [Fact]
    public async Task WhenAShortFieldFollowsALongOne_ShouldReadItExactly()
    {
        var records = await AfterTheLongFieldAsync();
        Assert.Equal("next"u8.ToArray(), (await records.ReadAsync(default))!.Value.ToArray());
    }

    [Fact]
    public async Task WhenEveryFieldIsRead_ShouldReturnNoRecord()
    {
        var records = await AfterBothFieldsAsync();
        Assert.Null(await records.ReadAsync(default));
    }

    [Fact]
    public async Task WhenAFieldIsOverTheLimit_ShouldFail()
    {
        var bytes = LongFieldThenNext();
        await Assert.ThrowsAsync<GitOutputLimitException>(async () => await new GitRecordStream(new MemoryStream(bytes), 100).ReadAsync(default));
    }

    [Fact]
    public async Task WhenTheLastRecordHasNoNul_ShouldFailAsIncomplete() =>
        await Assert.ThrowsAsync<FormatException>(async () => await new GitRecordStream(new MemoryStream("unterminated"u8.ToArray())).ReadAsync(default));

    [Fact]
    public async Task WhenARecordOfSeveralFieldsIsRead_ShouldGiveItsFieldsUpToTheLastNul()
    {
        var whole = EncodingRecord();
        var commits = TwoCommits(whole);
        Assert.Equal(whole[..^1], (await commits.ReadAsync(default, 6))!.Value.ToArray());
    }

    [Fact]
    public async Task WhenTheNextRecordOfSeveralFieldsIsRead_ShouldGiveItWholeToo()
    {
        var whole = EncodingRecord();
        var commits = TwoCommits(whole);
        await commits.ReadAsync(default, 6);
        Assert.Equal(whole[..^1], (await commits.ReadAsync(default, 6))!.Value.ToArray());
    }

    [Fact]
    public async Task WhenEveryRecordOfSeveralFieldsIsRead_ShouldReturnNoRecord()
    {
        var whole = EncodingRecord();
        var commits = TwoCommits(whole);
        await commits.ReadAsync(default, 6);
        await commits.ReadAsync(default, 6);
        Assert.Null(await commits.ReadAsync(default, 6));
    }

    private static byte[] EncodingRecord() => System.Text.Encoding.UTF8.GetBytes("id\0parents\0author\0email\0date\0" + new string('x', 20_000) + "\0");
}
