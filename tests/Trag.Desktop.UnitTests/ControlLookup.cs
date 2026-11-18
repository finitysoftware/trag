using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Trag.Desktop.UnitTests;

// Named controls live in the name scope of the view that declares them. The tests look them up through the visual tree
// instead, so a control can move into a dialog or a sub-view without the test changing.
internal static class ControlLookup
{
    public static T? Descendant<T>(this Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name);
}
