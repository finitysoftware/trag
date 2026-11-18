namespace Trag.Domain.Errors;

public sealed class RepositoryException(RepositoryError error) : Exception(error.ToString())
{
    public RepositoryError Error { get; } = error;
}
