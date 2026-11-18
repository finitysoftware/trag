using System.Text;
using System.Text.Json;
using Trag.Domain.Accounts;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Accounts;
using Trag.Infrastructure.Platform.Storage;

namespace Trag.Infrastructure.UnitTests.Platform.Storage;

// The five settings files Trag reads at start, each through its own store, holding what a hand edit, a crash or a broken
// disk can leave: the file the store wrote itself cut short at every byte, every value in it replaced by a hostile token,
// every key repeated before and after itself with such a token, seeded random bytes changed, a byte order mark and
// UTF-16. A store answers with a value or refuses the file with the exception its callers catch:
// JsonException or IOException for the files read through JsonFileStore, AccountException saying the settings could not
// be read for the accounts.
public sealed class JsonStoreFuzzTests : IDisposable
{
    private const int Seeds = 500;
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateTimeOffset At = new(2026, 9, 14, 9, 30, 0, TimeSpan.Zero);

    // Values of every JSON type, a null, an array holding a null, numbers past the range of an int and of a double, a lone
    // surrogate, a NUL, dates out of range, and a string past every length the stores check and past the size
    // settings.json, git.json and the layout may have.
    private static readonly string[] Tokens = ["null", "true", "0", "-1", "[]", "{}", "[null]", "\"\"", "\"text\"",
        "99999999999999999999", "1e400", "-1e400", "1.7976931348623157e308", "\"\\uD800\"", "\"\\u0000\"",
        "\"0000-01-01T00:00:00Z\"", "\"9999-12-31T23:59:59-14:00\"", "\"" + new string('x', 70_000) + "\""];

    private static readonly Dictionary<string, StoreFile> Stores = new()
    {
        ["settings.json"] = new(path => new LastRepositoryStore(path).SaveAsync(@"D:\Repos\Trag", Ct),
            async path => await new LastRepositoryStore(path).LoadAsync(Ct), Unreadable),
        ["settings.json.diff.json"] = new(path => new LayoutStore(path).SaveLayoutAsync(new(1, 0.62, 0.3, 2, 0.4, 0.55, 0.8, 5, 120), Ct),
            async path => await new LayoutStore(path).LoadLayoutAsync(Ct), Unreadable),
        ["git.json"] = new(path => new GitChoiceStore(path).SaveAsync(@"C:\Program Files\Git\cmd\git.exe", Ct),
            async path => await new GitChoiceStore(path).LoadAsync(Ct), Unreadable),
        ["repositories.json"] = new(path => new RepositoryCatalogStore(path).UpdateAsync(_ =>
            [new(@"D:\Repos\Trag", "Trag", At, At, "https://github.com/finitysoftware/Trag.git", "main"), new(@"D:\Repos\Atlas", "Atlas", At)], Ct),
            async path => await new RepositoryCatalogStore(path).LoadAsync(Ct), Unreadable),
        ["accounts.json"] = new(path => new AccountSettingsFile(path).UpdateAsync(_ => Accounts(), Ct),
            async path => await new AccountSettingsFile(path).LoadAsync(Ct), error => error is AccountException { Message: AccountFailureText.SettingsNotRead }),
    };

    private readonly string directory = Path.Combine(Path.GetTempPath(), "trag-store-fuzz-" + Guid.NewGuid().ToString("N"));

    public JsonStoreFuzzTests() => Directory.CreateDirectory(directory);
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    public static TheoryData<string> Files => [.. Stores.Keys];

    [Theory]
    [MemberData(nameof(Files))]
    public async Task WhenAStoreFileIsCutShortAtAnyByte_ShouldRefuseItAsUnreadable(string name)
    {
        var (store, path, sample) = await SampleAsync(name);
        await AssertOutcomesAsync(store, path, Enumerable.Range(0, sample.Length).Select(length => ($"cut to {length} bytes", sample[..length])), mayLoad: false);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task WhenAValueInAStoreFileHasTheWrongTypeOrAHugeNumber_ShouldLoadItOrRefuseItAsUnreadable(string name)
    {
        var (store, path, sample) = await SampleAsync(name);
        await AssertOutcomesAsync(store, path, from value in Values(sample)
            from token in Tokens
            select ($"{Excerpt(Encoding.UTF8.GetString(sample[value]))} at {value.Start} replaced by {Excerpt(token)}", Spliced(sample, value, token)), mayLoad: true);
    }

    // Which of the two values wins is the serializer's choice; either is a value the store has to take or refuse.
    [Theory]
    [MemberData(nameof(Files))]
    public async Task WhenAKeyInAStoreFileIsRepeated_ShouldLoadItOrRefuseItAsUnreadable(string name)
    {
        var (store, path, sample) = await SampleAsync(name);
        await AssertOutcomesAsync(store, path, from property in Properties(sample)
            from token in Tokens
            let repeated = $"\"{property.Name}\":{token}"
            from after in new[] { false, true }
            select ($"{property.Name} at {property.Start} repeated {(after ? "after" : "before")} itself as {Excerpt(token)}",
                after ? Spliced(sample, property.End..property.End, "," + repeated) : Spliced(sample, property.Start..property.Start, repeated + ",")), mayLoad: true);
    }

    [Theory]
    [InlineData("settings.json", 0, Seeds)]
    [InlineData("settings.json.diff.json", 0, Seeds)]
    [InlineData("git.json", 0, Seeds)]
    [InlineData("repositories.json", 0, Seeds)]
    [InlineData("accounts.json", 0, Seeds)]
    public async Task WhenBytesOfAStoreFileAreChangedAtRandom_ShouldLoadItOrRefuseItAsUnreadable(string name, int firstSeed, int seeds)
    {
        var (store, path, sample) = await SampleAsync(name);
        await AssertOutcomesAsync(store, path, Enumerable.Range(firstSeed, seeds).Select(seed => ($"[InlineData(\"{name}\", {seed}, 1)]", Changed(sample, seed))), mayLoad: true);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task WhenAStoreFileStartsWithAUtf8ByteOrderMark_ShouldLoadTheSameValue(string name)
    {
        var (store, path, sample) = await SampleAsync(name);
        var expected = await store.LoadAsync(path);
        await File.WriteAllBytesAsync(path, [.. Encoding.UTF8.GetPreamble(), .. sample], Ct);
        Assert.Equal(expected, await store.LoadAsync(path));
    }

    // As Notepad writes either kind of UTF-16, with its byte order mark.
    [Theory]
    [MemberData(nameof(Files))]
    public async Task WhenAStoreFileIsUtf16_ShouldRefuseItAsUnreadable(string name)
    {
        var (store, path, sample) = await SampleAsync(name);
        var text = Encoding.UTF8.GetString(sample);
        await AssertOutcomesAsync(store, path, new[] { Encoding.Unicode, Encoding.BigEndianUnicode }
            .Select(encoding => (encoding.EncodingName, encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray())), mayLoad: false);
    }

    // One settings file through its store: how the store writes its sample, what a load answers, and the refusal its
    // callers catch.
    private sealed record StoreFile(Func<string, Task> SaveSampleAsync, Func<string, Task<object?>> LoadAsync, Func<Exception, bool> Refuses);

    private static bool Unreadable(Exception error) => error is JsonException or IOException;

    // A GitHub account without an expiry and a GitLab one with it, both as a sign-in saves them.
    private static AccountInfo[] Accounts()
    {
        var gitLab = AccountMapping.GitLabApiBase("https://gitlab.example");
        return [new(AccountIdentity.StableId(AccountProvider.GitHub, AccountMapping.GitHubApi, "1"), AccountProvider.GitHub, "1", "alice",
                AccountMapping.GitHubApi, AccountMapping.GitHubOrigin, true, At, null),
            new(AccountIdentity.StableId(AccountProvider.GitLab, gitLab, "7"), AccountProvider.GitLab, "7", "bob",
                gitLab, AccountMapping.GitLabOrigin(gitLab), true, At, At.AddHours(2))];
    }

    // The file as the store wrote it.
    private async Task<(StoreFile Store, string Path, byte[] Sample)> SampleAsync(string name)
    {
        var store = Stores[name];
        var path = Path.Combine(directory, name);
        await store.SaveSampleAsync(path);
        return (store, path, await File.ReadAllBytesAsync(path, Ct));
    }

    // Each input written over the file and loaded: a value, when the input may load, or the store's refusal. Every other
    // outcome is reported once per exception type, with the first input that gave it.
    private static async Task AssertOutcomesAsync(StoreFile store, string path, IEnumerable<(string Case, byte[] Content)> inputs, bool mayLoad)
    {
        var failures = new Dictionary<string, string>();
        foreach (var (description, content) in inputs)
        {
            await File.WriteAllBytesAsync(path, content, Ct);
            var error = await Record.ExceptionAsync(() => store.LoadAsync(path));
            if (error is null ? mayLoad : store.Refuses(error)) continue;
            failures.TryAdd(error?.GetType().Name ?? "loaded", description + ": " + (error is null ? "loaded" : error.GetType().Name + ": " + error.Message));
        }
        Assert.True(failures.Count == 0, string.Join('\n', failures.Values));
    }

    // Where each value of the sample lies: the root, every container, every array element and every property's value.
    private static List<Range> Values(byte[] json)
    {
        var values = new List<Range>();
        var open = new Stack<int>();
        var reader = new Utf8JsonReader(json);
        while (reader.Read())
        {
            if (reader.TokenType is JsonTokenType.PropertyName) continue;
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) open.Push((int)reader.TokenStartIndex);
            else values.Add((reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray ? open.Pop() : (int)reader.TokenStartIndex)..(int)reader.BytesConsumed);
        }
        return values;
    }

    // Each property of the sample, from the quote that opens its name to the end of its value. A property's value ends
    // with the first value that ends at the property's own depth.
    private static List<(string Name, int Start, int End)> Properties(byte[] json)
    {
        var properties = new List<(string Name, int Start, int End)>();
        var pending = new Stack<(string Name, int Start, int Depth)>();
        var reader = new Utf8JsonReader(json);
        while (reader.Read())
        {
            if (reader.TokenType is JsonTokenType.PropertyName) pending.Push((reader.GetString()!, (int)reader.TokenStartIndex, reader.CurrentDepth));
            else if (reader.TokenType is not (JsonTokenType.StartObject or JsonTokenType.StartArray) && pending.TryPeek(out var property) && property.Depth == reader.CurrentDepth)
                properties.Add((pending.Pop().Name, property.Start, (int)reader.BytesConsumed));
        }
        return properties;
    }

    private static byte[] Spliced(byte[] sample, Range span, string text) => [.. sample[..span.Start], .. Encoding.UTF8.GetBytes(text), .. sample[span.End..]];

    // One to four bytes of the sample replaced by random ones; a seed gives the same input on every run.
    private static byte[] Changed(byte[] sample, int seed)
    {
        var random = new Random(seed);
        var changed = sample.ToArray();
        for (var count = random.Next(1, 5); count > 0; count--) changed[random.Next(changed.Length)] = (byte)random.Next(256);
        return changed;
    }

    private static string Excerpt(string text) => text.Length <= 40 ? text : text[..40] + "…";
}
