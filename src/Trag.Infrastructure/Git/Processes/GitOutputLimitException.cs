namespace Trag.Infrastructure.Git.Processes;

public sealed class GitOutputLimitException() : Exception("Git output exceeded the configured limit.");
