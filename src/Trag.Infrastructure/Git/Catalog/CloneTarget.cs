using Trag.Domain.Catalog;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;

namespace Trag.Infrastructure.Git.Catalog;

// Where a clone goes, decided before Git starts: the address Git will be given, the folder it has to create and the
// parent that must exist first. Every refusal is the sentence the picker shows, and a refused clone touches nothing.
internal sealed record CloneTarget(string Address, string Folder, string Parent)
{
    public static (CloneTarget? Target, string? Rejection) Resolve(CloneRequest request)
    {
        var address = request.CloneUrl.Trim();
        if (!IsSource(address)) return (null, CloneText.AddressInvalid);
        if (string.IsNullOrWhiteSpace(request.TargetFolder)) return (null, CloneText.FolderMissing);
        // A Windows name cannot end in white space, and making the path full drops it there; elsewhere it is part of the
        // folder asked for.
        var folder = request.TargetFolder.TrimStart();
        if (!Path.IsPathFullyQualified(folder)) return (null, CloneText.FolderNotFullPath);
        folder = Path.GetFullPath(folder);
        if (File.Exists(folder)) return (null, CloneText.FileInTheWay);
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()) return (null, CloneText.FolderNotEmpty);
        var parent = Path.GetDirectoryName(folder);
        if (string.IsNullOrEmpty(parent)) return (null, CloneText.FolderWithoutParent);
        return (new(address, folder, parent), null);
    }

    // A remote address follows the remote rules; a local repository is a folder that is there, named by its full path,
    // which may hold spaces. Neither starts with '-', so Git never reads the source as an option.
    private static bool IsSource(string address) => RemoteUrls.IsValidAddress(address)
        || !address.Any(char.IsControl) && Path.IsPathFullyQualified(address) && Directory.Exists(address);
}
