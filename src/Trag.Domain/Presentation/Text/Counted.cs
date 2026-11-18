using System.Globalization;

namespace Trag.Domain.Presentation.Text;

// A number as every text writes it (no grouping, no culture; a limit in the thousands grouped), a count with its noun,
// plural except for one, a size limit in whole kilobytes or megabytes, and a size in the largest unit it fills, with one
// decimal at most.
internal static class Counted
{
    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    public static string Grouped(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
    public static string Kilobytes(int bytes) => Number(bytes / 1024) + " KB";
    public static string Megabytes(int bytes) => Number(bytes / (1024 * 1024)) + " MB";
    public static string Of(int count, string noun) => Number(count) + " " + noun + (count == 1 ? "" : "s");

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => bytes == 1 ? "1 byte" : bytes.ToString(CultureInfo.InvariantCulture) + " bytes",
        < 1024 * 1024 => Unit(bytes, 1024) + " KB",
        < 1024 * 1024 * 1024 => Unit(bytes, 1024 * 1024) + " MB",
        _ => Unit(bytes, 1024 * 1024 * 1024) + " GB"
    };

    private static string Unit(long bytes, long unit) => (bytes / (double)unit).ToString("0.#", CultureInfo.InvariantCulture);
    public static string Uncommitted(int files) => Of(files, "file") + (files == 1 ? " has" : " have") + " uncommitted changes.";
    public static string InConflict(int open) => Of(open, "file") + (open == 1 ? " is" : " are") + " in conflict.";
}
