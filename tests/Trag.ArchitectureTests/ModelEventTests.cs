using System.Text.RegularExpressions;

namespace Trag.ArchitectureTests;

// A model's state is an observable property; an event is kept only where its order matters or it tells a moment rather
// than a state. The list is exact: a new event joins it with its reason, and one that goes leaves it.
public sealed partial class ModelEventTests
{
    private static readonly string[] Events =
    [
        "Trag.Application/Accounts/AccountsModel.cs Edited", // the user finished editing the accounts
        "Trag.Application/Catalog/CatalogModel.cs LinksChecked", // every link of the read was checked
        "Trag.Application/Catalog/LocalRepositoriesModel.cs Checked", // one link check answered
        "Trag.Application/History/CommitDetailsModel.cs Interrupted", // the reads in flight were dropped
        "Trag.Application/History/HistoryModel.cs Restarting", // the feed starts over
        "Trag.Application/Live/CancelRegistry.cs Changed", // what Cancel can reach changed
        "Trag.Application/Live/Repository.cs SnapshotApplied", // the parts follow a snapshot before any screen
        "Trag.Application/Live/Repository.cs Settled", // after every follower of the snapshot
        "Trag.Application/Live/RepositorySession.cs Closed", // the repository closed
        "Trag.Application/Live/Requery.cs Suggested", // the commands are asked again
        "Trag.Application/Live/Workbench.cs AccountsEdited", // the accounts were edited
        "Trag.Application/Status/StatusFiles.cs BatchStarting", // the list's edits, in order, for the virtualized list
        "Trag.Application/Status/StatusFiles.cs BatchCompleted",
        "Trag.Application/Status/StatusFiles.cs Edited",
        "Trag.Application/Status/StatusModel.cs Replaced", // new lists, a delta about to edit them, edited, published
        "Trag.Application/Status/StatusModel.cs Editing",
        "Trag.Application/Status/StatusModel.cs Edited",
        "Trag.Application/Status/StatusModel.cs Settled",
        "Trag.Application/Sync/RemotesModel.cs TargetFollowed", // which change it was: another target, or the same target's choice
        "Trag.Application/Sync/RemotesModel.cs ChoiceChanged",
        "Trag.Application/Sync/RemotesModel.cs Fetched", // a fetch of the target succeeded, and when: the automatic fetch counts it as its own
    ];

    [Fact]
    public void ModelsKeepEventsOnlyForAnOrderOrAMoment()
    {
        var events = RepositoryRoot.ProductionSources("Trag.Domain").Concat(RepositoryRoot.ProductionSources("Trag.Application"))
            .SelectMany(file => Declaration().Matches(CSharpSource.Strip(file.Text)).Select(match => file.Display + " " + match.Groups["name"].Value))
            .ToArray();
        Assert.Equal(Events.Order(StringComparer.Ordinal), events.Order(StringComparer.Ordinal));
    }

    [GeneratedRegex(@"\bevent\s+[\w.<>?,\s]+?\s(?<name>\w+)\s*;")]
    private static partial Regex Declaration();
}
