using Trag.Domain.Branches;

namespace Trag.ViewModels.Branches;

// A class on purpose: the dialog rebuilds the list after branches load and the selection must move to the new instance.
public sealed class StartPointOption(StartPoint point)
{
    public StartPoint Point { get; } = point;
    public string Display => Point.Display;
    public string? Ref => Point.Ref;
    public override string ToString() => Display;
}
