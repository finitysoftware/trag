using Trag.Application.Live;
using Trag.ViewModels.Repositories;
using Trag.ViewModels.Setup;
using Trag.ViewModels.Sync;

namespace Trag.ViewModels.Shell;

// What the shell shows whichever repository is open, for the window's whole lifetime: the application's models, the
// navigator, the repository list, the automatic fetch and the Git Trag runs.
public sealed record ApplicationFeatures(Workbench Workbench, Navigator Navigator, RepositoryCatalogViewModel Catalog, AutoFetchViewModel AutoFetch, GitSetupViewModel Git);
