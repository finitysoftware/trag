using Trag.Domain.Paths;
using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// An untracked file ignored by a line added to the repository's .gitignore (IgnoreChoices); Git is then asked about the file.
public sealed record IgnoreRequest(RepositorySnapshot Expected, GitPath Path, string Pattern) : GitWriteRequest(Expected);
