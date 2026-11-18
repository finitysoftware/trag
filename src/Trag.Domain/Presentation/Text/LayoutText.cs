namespace Trag.Domain.Presentation.Text;

// What the status bar says when the saved diff layout could not be read or saved; the layout on screen stays usable.
public static class LayoutText
{
    public const string NotLoaded = "Saved diff layout could not be loaded. Using the default layout.";
    public const string NotSaved = "The diff layout could not be saved. This session's layout is still available.";
}
