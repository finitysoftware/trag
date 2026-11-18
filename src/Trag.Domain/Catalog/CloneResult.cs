namespace Trag.Domain.Catalog;

// How a clone ended, in the dialog's words and with Git's details; a clone that joined the list carries that addition.
public sealed record CloneResult(bool Succeeded, string Message, string Details = "")
{
    public CatalogAddition? Addition { get; init; }
}
