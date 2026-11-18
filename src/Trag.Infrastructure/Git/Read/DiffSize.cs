namespace Trag.Infrastructure.Git.Read;

// What a file's objects allow its diff to be: whole, a large file's bounded preview, or nothing, because a version it
// needs is not in this repository.
internal enum DiffSize { Fits, Large, NotDownloaded }
