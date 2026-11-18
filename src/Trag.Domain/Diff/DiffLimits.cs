namespace Trag.Domain.Diff;

// How much of a diff or a file the application is willing to hold and show. The readers ask Git for no more than this.
public static class DiffLimits
{
    public const int ByteLimit = 5 * 1024 * 1024;
    public const int LineLimit = 50_000;
    public const int LineLengthLimit = 8_192;
    public const int PreviewLines = 200;
    // How much of a file too large for a whole diff a bounded content preview reads.
    public const int PreviewBytes = 64 * 1024;
}
