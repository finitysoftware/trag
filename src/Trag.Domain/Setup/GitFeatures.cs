using Trag.Domain.Status;

namespace Trag.Domain.Setup;

// What a Git brings by its configuration outside any repository: Git LFS, the helper that keeps credentials, and how it
// signs commits.
public sealed record GitFeatures(bool Lfs, string? CredentialHelper, CommitSigning Signing);
