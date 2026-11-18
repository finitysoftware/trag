namespace Trag.Domain.Presentation;

// The part of the workspace that is open. The navigator holds the current one; Domain names the sections because its
// texts say which view resolves a conflict (ConflictScope).
public enum WorkspaceSection { FileStatus, History, Search, Merge, Rebase, Repositories, Conflicts, Pick }
