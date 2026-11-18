using System.Text;
using Trag.Domain.Diff;

namespace Trag.TestSupport;

// The shared corpus of file contents at the edges of text handling. Theories iterate the names and
// ask for the bytes inside the test: every call builds a fresh array, so no test changes another's sample, and a 5 MiB
// array never reaches test discovery. The samples one line and one byte over the diff limits are sized from DiffLimits,
// each over its own limit only; the 100 000-character line is a literal far over the line-length limit.
public static class TestContents
{
    private static readonly (string Name, Func<byte[]> Build)[] Samples =
    [
        ("empty", () => []),
        ("one byte without a newline", () => [.. "x"u8]),
        ("only a newline", () => [.. "\n"u8]),
        ("CRLF", () => [.. "first\r\nsecond\r\n"u8]),
        ("LF", () => [.. "first\nsecond\n"u8]),
        ("mixed CRLF and LF", () => [.. "first\r\nsecond\nthird\r\n"u8]),
        ("CR only", () => [.. "first\rsecond\rthird\r"u8]),
        ("UTF-8 with a BOM", () => [0xEF, 0xBB, 0xBF, .. "first\nsecond\n"u8]),
        ("UTF-16 LE with a BOM", () => [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("first\r\nsecond\r\n")]),
        // "Čađava šuma\nžuto\n" in ISO-8859-2, written as bytes so no test process has to register a code page provider.
        ("Latin-2", () => [0xC8, .. "a"u8, 0xF0, .. "ava "u8, 0xB9, .. "uma\n"u8, 0xBE, .. "uto\n"u8]),
        ("invalid UTF-8 mid-line", () => [.. "before č "u8, 0xC3, 0x28, .. " after\n"u8]),
        // The first byte past the 8 000 in which Git's diff looks for a NUL to call a file binary.
        ("NUL as the 8 001st byte", () => [.. Lines(100, 79), 0, .. "after the window\n"u8]),
        ("a line of 100 000 characters", () => Lines(1, 100_000)),
        ("one line over the diff line limit", () => Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(0, DiffLimits.LineLimit + 1).Select(i => i + "\n")))),
        ("one byte over the diff byte limit", () => [.. Lines(DiffLimits.ByteLimit / 128, 127), (byte)'\n']),
    ];

    public static IReadOnlyList<string> Hostile { get; } = [.. Samples.Select(sample => sample.Name)];

    public static byte[] Bytes(string name) => Samples.Single(sample => sample.Name == name).Build();

    private static byte[] Lines(int count, int length) => Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat(new string('x', length) + "\n", count)));
}
