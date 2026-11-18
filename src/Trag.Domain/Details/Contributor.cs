using Trag.Domain.Writes;

namespace Trag.Domain.Details;

// One person behind the current branch, as .mailmap names them, with the commits they wrote.
public sealed record Contributor(GitIdentity Identity, int Commits);
