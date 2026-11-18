namespace Trag.Domain.Publish;

// A namespace the account may create repositories in. The account user itself is added by PublishOwners.
public sealed record PublishOwner(string Id, string Display, bool IsUser = false)
{
    // The organization a repository is created in; null for the account's own namespace.
    public string? OrganizationId => IsUser ? null : Id;
    public override string ToString() => Display;
}
