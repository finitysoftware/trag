using Trag.Domain.Presentation.Text;

namespace Trag.Domain.Tags;

// The repository's tags as the sidebar shows them, in the order Git lists them: the button names the first and counts
// them once there is more than one, and a search keeps the tags whose name holds the text in any case.
public static class TagList
{
    public static string ButtonText(IReadOnlyList<TagInfo> tags) => tags.Count == 0 ? TagText.Button : tags[0].Name;

    public static string CountText(IReadOnlyList<TagInfo> tags) => tags.Count > 1 ? Counted.Number(tags.Count) : "";

    public static IReadOnlyList<TagInfo> Filter(IReadOnlyList<TagInfo> tags, string search) =>
        search.Trim() is { Length: > 0 } query ? [.. tags.Where(tag => tag.Name.Contains(query, StringComparison.OrdinalIgnoreCase))] : tags;
}
