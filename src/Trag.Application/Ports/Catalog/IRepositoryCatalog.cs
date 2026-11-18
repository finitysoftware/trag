namespace Trag.Application.Ports.Catalog;

// The four catalog ports as one object: the composition root builds one catalog and hands it to the workbench, whose
// catalog model passes each part the narrow port it takes.
public interface IRepositoryCatalog : IRepositoryCatalogStore, IRepositoryProber, IRemoteRepositoryDirectory, IRepositoryCreator;
