using Trag.Domain.Writes;

namespace Trag.Domain.Presentation.Text;

// One person in the Repository details dialog: who they are, and their commits as the dialog writes them.
public sealed record ContributorRow(GitIdentity Identity, string Commits);
