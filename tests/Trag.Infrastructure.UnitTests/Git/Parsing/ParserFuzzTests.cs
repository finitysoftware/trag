using System.Diagnostics;
using System.Text;

namespace Trag.Infrastructure.UnitTests.Git.Parsing;

// The parsers of outside input, each through the entry point Trag uses for what arrives from Git, the askpass
// pipe or a provider, fed input no test writes by hand: random bytes, a valid sample with one byte changed, and a valid
// sample cut short. Each input is answered, or refused with an exception the parser's callers handle, within 100 ms.
// An input comes from its seed and the target's samples alone, so a seed that once broke a parser stays as a row of the
// regression theory.
public sealed class ParserFuzzTests
{
    public enum Generator { RandomBytes, OneByteChanged, CutShort }

    private const int Iterations = 10_000;
    // The sample a seed starts from is seed / SampleSeeds, so a sample appended later takes seeds of its own.
    private const int SampleSeeds = 1_000_000;
    // A slow input is measured again before it counts, so a machine busy with other tests does not fail it.
    private const int SlowRetries = 3;
    private static readonly TimeSpan InputLimit = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ParserLimit = TimeSpan.FromMinutes(1);

    public static TheoryData<string> Parsers => [.. ParserFuzzTargets.All.Keys];

    [Theory]
    [MemberData(nameof(Parsers))]
    public void WhenAParserReadsItsValidSample_ShouldAnswerWithoutRejectingIt(string parser)
    {
        var target = ParserFuzzTargets.All[parser];
        foreach (var sample in target.Samples) target.Read(sample);
    }

    [Theory]
    [MemberData(nameof(Parsers))]
    public Task WhenAParserReadsRandomBytes_ShouldAnswerOrRejectEveryInputWithAnAllowedExceptionWithin100Ms(string parser) =>
        FuzzAsync(parser, Generator.RandomBytes);

    [Theory]
    [MemberData(nameof(Parsers))]
    public Task WhenAParserReadsAValidSampleWithOneByteChanged_ShouldAnswerOrRejectEveryInputWithAnAllowedExceptionWithin100Ms(string parser) =>
        FuzzAsync(parser, Generator.OneByteChanged);

    [Theory]
    [MemberData(nameof(Parsers))]
    public Task WhenAParserReadsAValidSampleCutShort_ShouldAnswerOrRejectEveryInputWithAnAllowedExceptionWithin100Ms(string parser) =>
        FuzzAsync(parser, Generator.CutShort);

    [Theory]
    [InlineData("Patch", Generator.CutShort, 15)]
    [InlineData("Patch", Generator.CutShort, 170)]
    [InlineData("Patch", Generator.OneByteChanged, 2002249)]
    [InlineData("Blame", Generator.RandomBytes, 118)]
    [InlineData("Blame", Generator.OneByteChanged, 1)]
    [InlineData("Blame", Generator.OneByteChanged, 174)]
    [InlineData("Blame", Generator.OneByteChanged, 371)]
    [InlineData("AskPass", Generator.OneByteChanged, 7)]
    [InlineData("GitVersion", Generator.OneByteChanged, 1002759)]
    public void WhenAnInputTheFuzzerOnceBrokeIsReadAgain_ShouldAnswerOrRejectItWithAnAllowedException(string parser, Generator generator, int seed)
    {
        var target = ParserFuzzTargets.All[parser];
        var input = Input(target, generator, seed);

        Assert.Null(Failure(target, input));
    }

    // Every distinct failure, by exception and the frame of Trag's code it came from, with the first seed that found it.
    private static async Task FuzzAsync(string parser, Generator generator)
    {
        var target = ParserFuzzTargets.All[parser];
        var failures = new Dictionary<string, (int Seed, byte[] Input)>();
        var seed = -1;
        var run = Task.Run(() =>
        {
            foreach (var next in Seeds(target, generator))
            {
                Volatile.Write(ref seed, next);
                var input = Input(target, generator, next);
                if (Failure(target, input) is { } failure) failures.TryAdd(failure, (next, input));
            }
        });
        try { await run.WaitAsync(ParserLimit); }
        catch (TimeoutException) { Assert.Fail($"{parser} was still reading seed {Volatile.Read(ref seed)} of {generator} after {ParserLimit}."); }

        Assert.True(failures.Count == 0, string.Join('\n', failures.Select(failure =>
            $"[InlineData(\"{parser}\", Generator.{generator}, {failure.Value.Seed})] {failure.Key}: {Escaped(failure.Value.Input)}")));
    }

    private static IEnumerable<int> Seeds(ParserFuzzTarget target, Generator generator)
    {
        if (generator == Generator.RandomBytes) return Enumerable.Range(0, Iterations);
        var perSample = (Iterations + target.Samples.Length - 1) / target.Samples.Length;
        return Enumerable.Range(0, target.Samples.Length).SelectMany(sample => Enumerable.Range(sample * SampleSeeds, perSample));
    }

    // Up to 4 KB of random bytes, a sample with one byte replaced by another, or a sample's first 0 to n - 1 bytes.
    private static byte[] Input(ParserFuzzTarget target, Generator generator, int seed)
    {
        var random = new Random(seed);
        if (generator == Generator.RandomBytes)
        {
            var bytes = new byte[random.Next(4097)];
            random.NextBytes(bytes);
            return bytes;
        }
        var sample = target.Samples[seed / SampleSeeds];
        if (generator == Generator.CutShort) return sample[..random.Next(sample.Length)];
        var changed = sample.ToArray();
        var at = random.Next(changed.Length);
        changed[at] = (byte)(changed[at] + random.Next(1, 256));
        return changed;
    }

    // An exception the target does not allow, or a read over the limit each time it is measured; null for an answer or a
    // refusal in time.
    private static string? Failure(ParserFuzzTarget target, byte[] input)
    {
        for (var run = 0; run <= SlowRetries; run++)
        {
            var watch = Stopwatch.StartNew();
            var error = Record.Exception(() => target.Read(input));
            watch.Stop();
            if (error is not null && !target.Allows(error)) return error.GetType().Name + " at " + Frame(error);
            if (watch.Elapsed <= InputLimit) return null;
        }
        return $"over {InputLimit.TotalMilliseconds} ms on each of {SlowRetries + 1} runs";
    }

    // The innermost frame of Trag's own code: where the parser let the exception out.
    private static string Frame(Exception error) =>
        new StackTrace(error, true).GetFrames().FirstOrDefault(frame => frame.GetMethod()?.DeclaringType?.FullName?.StartsWith("Trag.", StringComparison.Ordinal) == true) is { } frame
            ? $"{frame.GetMethod()!.DeclaringType!.FullName}.{frame.GetMethod()!.Name}:{frame.GetFileLineNumber()}"
            : error.TargetSite?.ToString() ?? "an unknown frame";

    private static string Escaped(byte[] input)
    {
        var text = new StringBuilder();
        foreach (var b in input.Take(160)) text.Append(b is >= 0x20 and < 0x7F and not (byte)'\\' ? ((char)b).ToString() : $"\\x{b:X2}");
        return text.Append(input.Length > 160 ? $"… ({input.Length} bytes)" : "").ToString();
    }
}
