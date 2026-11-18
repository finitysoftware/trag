namespace Trag.Domain.Publish;

// A branch a publish may push into on one remote; new when the remote does not have it yet.
public sealed record PublishTarget(string Name, string Remote, bool IsNew)
{
    // Publishing into a branch the remote already has under another name pushes this branch's commits into it.
    public bool PushesIntoOther(string? branch) => !IsNew && Name != branch;
}
