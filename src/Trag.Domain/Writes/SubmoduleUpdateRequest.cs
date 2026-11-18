using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// Every submodule checked out at the commit the branch records, the ones never checked out cloned first.
public sealed record SubmoduleUpdateRequest(RepositorySnapshot Expected) : GitWriteRequest(Expected);
