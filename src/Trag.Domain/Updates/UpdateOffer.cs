namespace Trag.Domain.Updates;

// What the update service answered for this Trag: whether a newer release is out, which one, and where it is downloaded.
// Only a whole answer is one: a newer release needs its version and a download on Finity Software's own site over HTTPS.
public sealed record UpdateOffer(bool IsAvailable, string LatestVersion, Uri? Download)
{
    public static UpdateOffer? From(bool available, string? latest, string? download)
    {
        if (!available) return new(false, latest ?? "", null);
        if (string.IsNullOrWhiteSpace(latest) || !Uri.TryCreate(download, UriKind.Absolute, out var url) || !FinitySoftware.Owns(url)) return null;
        return new(true, latest, url);
    }
}
