using Trag.Application.Ports.Catalog;
using Trag.Domain.Catalog;
using Trag.Domain.Presentation.Text;

namespace Trag.Application.Catalog;

// A clone from the repository picker: Git clones into the target, then the clone joins the repository list. A clone Git
// finished is recorded even when Cancel arrives afterwards, so the list step does not take the token. A clone that was
// cancelled, that Git refused, or whose result could not be read or listed says so and changes nothing else.
public sealed class CloneWorkflow(IRepositoryCreator? cloner, LocalRepositoryCatalog catalog)
{
    // Where the clone is proposed to go: beside the most recently opened repository of the list (CloneTargets).
    public string SuggestTarget(string cloneUrl, string owner, string name) =>
        CloneTargets.Suggest(cloneUrl, owner, name, catalog.Entries.Ordered, cloner?.HomeFolder ?? "");

    public async Task<CloneResult> RunAsync(CloneRequest request, CancellationToken ct)
    {
        if (cloner is null) throw new InvalidOperationException("This catalog cannot clone.");
        CloneResult result;
        try { result = await cloner.CloneAsync(request, ct); }
        catch (OperationCanceledException) { return new(false, CloneText.Canceled); }
        catch (Exception) { return new(false, CatalogText.CloneNotDisplayed); }
        if (!result.Succeeded) return result;
        var added = await catalog.AddPathAsync(request.TargetFolder, CancellationToken.None);
        return added.Outcome is CatalogAdditionOutcome.Failed or CatalogAdditionOutcome.Canceled
            ? new(false, CatalogText.CloneNotDisplayed)
            : new(true, CatalogText.Cloned) { Addition = added };
    }
}
