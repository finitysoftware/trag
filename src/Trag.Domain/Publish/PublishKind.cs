namespace Trag.Domain.Publish;

// Publishing gives a local branch a remote to push to: a repository created on a provider account,
// an address the user already has, or a remote that exists but does not track this branch yet.
public enum PublishKind { CreateRepository, AddRemote, ExistingRemote }
