using Trag.Domain.Sync;

namespace Trag.Domain.Presentation.Text;

// One choice of the automatic fetch interval as Settings lists it.
public sealed record AutoFetchOption(AutoFetchInterval Interval, string Label);
