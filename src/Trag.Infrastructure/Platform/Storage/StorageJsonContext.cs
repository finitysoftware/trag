using System.Text.Json.Serialization;
using Trag.Domain.Presentation;

namespace Trag.Infrastructure.Platform.Storage;

// One generated manifest for every document this folder keeps on disk. A context is code generation, not a model, and
// three of them generated three copies of the same machinery.
[JsonSerializable(typeof(LastRepositorySettings))]
[JsonSerializable(typeof(SavedLayout))]
[JsonSerializable(typeof(RepositoryCatalogDocument))]
[JsonSerializable(typeof(GitChoiceSettings))]
internal sealed partial class StorageJsonContext : JsonSerializerContext;
