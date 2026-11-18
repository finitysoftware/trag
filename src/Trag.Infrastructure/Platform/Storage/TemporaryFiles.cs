namespace Trag.Infrastructure.Platform.Storage;

// Cleanup of an atomic-write temporary file must never replace the exception that made the write fail.
public static class TemporaryFiles
{
    public static void Delete(string? path)
    {
        if (path is null) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
