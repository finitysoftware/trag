namespace Trag.Application.Catalog;

// What the folder picker gave: a folder, nothing when the user closed it, or a picker that could not open, which the
// caller words for its dialog.
public sealed record FolderPick(string? Path, bool Failed)
{
    public static async Task<FolderPick> AskAsync(Func<Task<string?>> pick)
    {
        try { return new(await pick(), false); }
        catch (OperationCanceledException) { return new(null, false); }
        catch (Exception) { return new(null, true); }
    }
}
