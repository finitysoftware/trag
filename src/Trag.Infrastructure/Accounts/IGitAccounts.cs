using Trag.Application.Ports.Catalog;

namespace Trag.Infrastructure.Accounts;

// The account service as the Git layer sees it: which accounts exist, what they can reach, a broker for one
// destination, and a new repository on the hosting site. Signing in, verifying and removing an account are not
// reachable from here. Each part inside the Git layer takes only the one port it uses; the two facades are handed the
// whole object because they build those parts.
public interface IGitAccounts : ICredentialBrokerSource, IHostedRepositoryCreator, IRemoteRepositoryDirectory;
