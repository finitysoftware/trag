namespace Trag.Domain.Sync;

// What a transfer is judged by: the tip the remote reported before it and the tip it must report afterwards. A push
// expects its commit where the old tip was, a deletion expects no branch where a tip was, and a publish expects its
// commit where it read nothing beforehand. The verdict comes from what the remote says now, never from what Git said
// about the transfer, because the server may have finished the work before the connection was lost.
public sealed record RemoteTipCheck(string? Before, string? Expected)
{
    // exitCode: Git's, null when Git did not finish; failed: the transfer stopped on an exception.
    public RemoteTipVerdict Judge(string? after, int? exitCode, bool failed)
    {
        if (after == Expected) return RemoteTipVerdict.Arrived;
        if (after != Before) return RemoteTipVerdict.Changed;
        if (failed || exitCode is null) return RemoteTipVerdict.Interrupted;
        // Git reported success and the remote still says what it said before: that is not an answer Trag can trust.
        return exitCode == 0 ? RemoteTipVerdict.Changed : RemoteTipVerdict.Refused;
    }
}
