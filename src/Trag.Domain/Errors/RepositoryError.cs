namespace Trag.Domain.Errors;

public enum RepositoryError { PathUnavailable, NotRepository, UnsafeDirectory, BareRepository, GitMissing, GitTooOld, InvalidGitVersion, ReadFailed, OutputTooLarge, ChangedDuringRead }
