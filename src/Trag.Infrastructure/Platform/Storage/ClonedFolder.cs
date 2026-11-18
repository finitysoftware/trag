namespace Trag.Infrastructure.Platform.Storage;

// A folder a clone stopped half way wrote into, taken back so the same clone can start again: the folder goes, or is
// emptied when it was there before. Git marks its packs read-only. What cannot go stays, and the answer is whether
// nothing did.
internal static class ClonedFolder
{
    public static bool TakeBack(string folder, bool existed)
    {
        try
        {
            if (!Directory.Exists(folder)) return true;
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            if (!existed) Directory.Delete(folder, true);
            else foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                if (entry is DirectoryInfo inner) inner.Delete(true); else entry.Delete();
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }
}
