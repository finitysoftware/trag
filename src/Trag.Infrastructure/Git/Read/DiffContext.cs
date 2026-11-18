using System.Globalization;
using Trag.Domain.Diff;

namespace Trag.Infrastructure.Git.Read;

// The context Git is asked for around changes: as many lines as a diff may have, so a file that fits arrives whole, as
// one hunk, instead of its changes with the lines between them left out.
internal static class DiffContext
{
    public static readonly string WholeFile = "--unified=" + DiffLimits.LineLimit.ToString(CultureInfo.InvariantCulture);
}
