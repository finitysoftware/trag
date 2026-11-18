namespace Trag.Infrastructure.Updates;

// The update service's answer as it arrives; UpdateOffer decides whether it is a whole one.
internal sealed record UpdateResponse(bool? UpdateAvailable, string? LatestVersion, string? DownloadUrl);
