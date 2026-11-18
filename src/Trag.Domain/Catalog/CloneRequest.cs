namespace Trag.Domain.Catalog;

public sealed record CloneRequest(string CloneUrl, string TargetFolder, string? AccountId = null, IProgress<string>? Progress = null, bool Submodules = false);
