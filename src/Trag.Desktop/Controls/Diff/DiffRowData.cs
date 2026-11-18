using System.Globalization;
using Trag.Domain.Diff;

namespace Trag.Desktop.Controls.Diff;

public sealed record DiffRowData(object Identity, int Index, bool Split)
{
    public string Numbers(PatchLine line, bool right, int digits)
    {
        if (Split) return Format(right ? line.NewNumber : line.OldNumber);
        return Format(line.OldNumber).PadLeft(digits) + " " + Format(line.NewNumber).PadLeft(digits);
    }

    private static string Format(int? number) => number?.ToString(CultureInfo.InvariantCulture) ?? "";
}
