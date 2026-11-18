namespace Trag.Domain.Branches;

// The branches of one remote as the picker lists them; an empty remote name groups refs Git lists under no remote.
public sealed record RemoteBranchGroup(string Remote, IReadOnlyList<BranchInfo> Branches);
