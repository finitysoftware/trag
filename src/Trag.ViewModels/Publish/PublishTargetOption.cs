using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;

namespace Trag.ViewModels.Publish;

// A class on purpose: the list is rebuilt when branches load and the selection must move to the new instance.
public sealed class PublishTargetOption(PublishTarget target)
{
    public PublishTarget Target { get; } = target;
    public string Name => Target.Name;
    public string Remote => Target.Remote;
    public bool IsNew => Target.IsNew;
    public string Display => PublishText.TargetDisplay(Target);
    public override string ToString() => Display;
}
