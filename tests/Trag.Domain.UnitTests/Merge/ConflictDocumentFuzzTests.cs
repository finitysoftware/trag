using Trag.Domain.Merge;
using Trag.TestSupport;
using Xunit.Sdk;

namespace Trag.Domain.UnitTests.Merge;

// Conflicted files in the shape Git writes them, generated from fixed seeds and read back. A failure names the seed and
// the file, so the case can be replayed and kept as an example of its own.
public sealed class ConflictDocumentFuzzTests
{
    // A file with no line that opens a conflict is read as one with no block, not refused, whatever its endings, its byte
    // order mark or its other marker lines; the merge view then says no block is left.
    [Fact]
    public void WhenATextHasNoLineThatOpensAConflict_ShouldReadItWithNoBlock()
    {
        for (var seed = 0; seed < 10_000; seed++)
        {
            var random = new Random(seed);
            var text = new ConflictedFileShape(random, new[] { "LF", "CRLF", "Mixed" }[random.Next(3)]).WithoutConflicts();
            Check(seed, text, () => ConflictDocument.Parse(text) is { Blocks.Count: 0 });
        }
    }

    // TakeBlockAsync reads the file again before every block it takes, so each take works on what the takes before it
    // left. Taken in any order, each as any side, the blocks leave their chosen sides and every character outside them
    // as it was.
    [Theory]
    [InlineData("LF")]
    [InlineData("CRLF")]
    [InlineData("Mixed")]
    public void WhenEveryBlockIsTakenInAnyOrderWithAnySide_ShouldKeepEveryCharacterOutsideTheBlocks(string endings)
    {
        for (var seed = 0; seed < 3_500; seed++)
        {
            var random = new Random(seed);
            var file = new ConflictedFileShape(random, endings).Conflicted();
            var choices = file.Sides.Select(_ => (ConflictChoice)random.Next(3)).ToArray();
            var order = Enumerable.Range(0, choices.Length).ToArray();
            random.Shuffle(order);
            Check(seed, file.Text, () =>
            {
                var text = file.Text;
                for (var step = 0; step < order.Length; step++)
                {
                    if (ConflictDocument.Parse(text) is not { } document || document.Blocks.Count != order.Length - step) return false;
                    var block = order[step];
                    text = document.Take(block - order[..step].Count(taken => taken < block), choices[block]);
                }
                return text == file.Resolved(choices);
            });
        }
    }

    private static void Check(int seed, string file, Func<bool> holds)
    {
        var failure = $"seed {seed}: {file.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t").Replace(ConflictedFileShape.Bom, "\\uFEFF")}";
        bool held;
        try
        {
            held = holds();
        }
        catch (Exception e)
        {
            throw new XunitException(failure, e);
        }
        Assert.True(held, failure);
    }
}
