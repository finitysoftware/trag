using System.Security.Cryptography;

namespace Trag.Infrastructure.Git.State;

public static class IndexFingerprint
{
    public static async Task<string> ReadAsync(string gitDirectory, CancellationToken ct)
    {
        try
        {
            await using var file = new FileStream(Path.Combine(gitDirectory, "index"), FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Convert.ToHexString(await SHA256.HashDataAsync(file, ct).ConfigureAwait(false));
        }
        catch (FileNotFoundException) { return "absent"; }
    }
}
