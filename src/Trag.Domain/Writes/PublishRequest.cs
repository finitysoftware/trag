using Trag.Domain.Publish;
using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// Creating or naming the remote, recording the upstream and sending the branch, in one run.
public sealed record PublishRequest(RepositorySnapshot Expected, PublishOperation Publish) : GitWriteRequest(Expected);
