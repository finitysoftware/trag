using Avalonia.Media;

namespace Trag.Desktop.Controls.Icons;

// The line glyphs of the app on a 24 x 24 grid, by kind. An unknown kind draws the file glyph; a test checks that every
// kind used in the XAML exists here.
public static class IconGeometry
{
    private static readonly Dictionary<string, Geometry> Shapes = new()
    {
        ["open"] = Geometry.Parse("M3,7 L9,7 L11,10 L21,10 L19,20 L3,20 Z M3,7 L3,4 L10,4 L12,7 L19,7 L19,10"),
        ["commit"] = Geometry.Parse("M3,12 L8,12 M16,12 L21,12 M16,12 A4,4 0 1 1 8,12 A4,4 0 1 1 16,12"),
        ["pull"] = Geometry.Parse("M12,3 L12,15 M7,10 L12,15 L17,10 M4,16 L4,21 L20,21 L20,16"),
        ["push"] = Geometry.Parse("M12,16 L12,4 M7,9 L12,4 L17,9 M4,16 L4,21 L20,21 L20,16"),
        ["fetch"] = Geometry.Parse("M7,3 L7,17 M3,13 L7,17 L11,13 M17,21 L17,7 M13,11 L17,7 L21,11"),
        ["refresh"] = Geometry.Parse("M20,8 A9,9 0 1 0 21,15 M20,3 L20,8 L15,8"),
        ["close"] = Geometry.Parse("M6,6 L18,18 M18,6 L6,18"),
        ["file"] = Geometry.Parse("M5,3 L14,3 L19,8 L19,21 L5,21 Z M14,3 L14,8 L19,8 M8,12 L16,12 M8,16 L14,16"),
        ["history"] = Geometry.Parse("M4,8 A9,9 0 1 1 3,15 M4,3 L4,8 L9,8 M12,7 L12,12 L16,14"),
        ["search"] = Geometry.Parse("M17,10 A7,7 0 1 1 3,10 A7,7 0 1 1 17,10 M15,15 L21,21"),
        ["merge"] = Geometry.Parse("M6,21 L6,9 A9,9 0 0 0 15,18 M9,6 A3,3 0 1 1 3,6 A3,3 0 1 1 9,6 M21,18 A3,3 0 1 1 15,18 A3,3 0 1 1 21,18"),
        ["arrow"] = Geometry.Parse("M4,12 L20,12 M14,6 L20,12 L14,18"),
        ["rebase"] = Geometry.Parse("M6,3 L6,21 M6,15 Q6,9 12,9 L17,9 M14,6 L17,9 L14,12 M20,15 A2,2 0 1 1 16,15 A2,2 0 1 1 20,15"),
        ["skip"] = Geometry.Parse("M5,5 L13,12 L5,19 Z M17,5 L17,19"),
        ["swap"] = Geometry.Parse("M16,3 L20,7 L16,11 M20,7 L4,7 M8,13 L4,17 L8,21 M4,17 L20,17"),
        ["info"] = Geometry.Parse("M21,12 A9,9 0 1 1 3,12 A9,9 0 1 1 21,12 M12,11 L12,16 M12,8 L12,8.2"),
        ["warning"] = Geometry.Parse("M12,3.5 L21.5,20 L2.5,20 Z M12,10 L12,14 M12,17 L12,17.2"),
        ["blocked"] = Geometry.Parse("M21,12 A9,9 0 1 1 3,12 A9,9 0 1 1 21,12 M5.6,5.6 L18.4,18.4"),
        ["branch"] = Geometry.Parse("M6,7 L6,17 M18,7 L18,10 Q18,13 12,13 Q6,13 6,17 M9,4 A3,3 0 1 1 3,4 A3,3 0 1 1 9,4 M9,20 A3,3 0 1 1 3,20 A3,3 0 1 1 9,20 M21,4 A3,3 0 1 1 15,4 A3,3 0 1 1 21,4"),
        ["plus"] = Geometry.Parse("M12,5 L12,19 M5,12 L19,12"),
        ["minus"] = Geometry.Parse("M5,12 L19,12"),
        ["grip"] = Geometry.Parse("M7,8 L17,8 M7,12 L17,12 M7,16 L17,16"),
        ["trash"] = Geometry.Parse("M4,7 L20,7 M9,7 L9,4 L15,4 L15,7 M6,7 L7,20 L17,20 L18,7 M10,11 L10,16 M14,11 L14,16"),
        ["folder"] = Geometry.Parse("M3,6 L9,6 L11,9 L21,9 L21,19 L3,19 Z"),
        ["monitor"] = Geometry.Parse("M3,5 L21,5 L21,16 L3,16 Z M9,20 L15,20 M12,16 L12,20"),
        ["cloud"] = Geometry.Parse("M7,18 A4,4 0 1 1 8,10 A5,5 0 0 1 17,11 A3.5,3.5 0 1 1 17,18 Z"),
        ["publish"] = Geometry.Parse("M8,18 L7,18 A4,4 0 1 1 8,10 A5,5 0 0 1 17,11 A3.5,3.5 0 1 1 17,18 L16,18 M12,21 L12,12 M8.5,15.5 L12,12 L15.5,15.5"),
        ["globe"] = Geometry.Parse("M21,12 A9,9 0 1 1 3,12 A9,9 0 1 1 21,12 M3,12 L21,12 M12,3 Q7.5,7.5 7.5,12 Q7.5,16.5 12,21 M12,3 Q16.5,7.5 16.5,12 Q16.5,16.5 12,21"),
        ["check"] = Geometry.Parse("M5,12 L10,17 L19,7"),
        ["link"] = Geometry.Parse("M10,14 A4,4 0 0 0 15.5,14 L18,11.5 A4,4 0 0 0 12.5,6 L11,7.5 M14,10 A4,4 0 0 0 8.5,10 L6,12.5 A4,4 0 0 0 11.5,18 L13,16.5"),
        ["clock"] = Geometry.Parse("M21,12 A9,9 0 1 1 3,12 A9,9 0 1 1 21,12 M12,7 L12,12 L16,14"),
        ["lock"] = Geometry.Parse("M6,11 L18,11 L18,21 L6,21 Z M8,11 L8,7 A4,4 0 0 1 16,7 L16,11"),
        ["external"] = Geometry.Parse("M14,4 L20,4 L20,10 M20,4 L11,13 M18,14 L18,20 L4,20 L4,6 L10,6"),
        ["back"] = Geometry.Parse("M15,5 L8,12 L15,19"),
        ["chevron"] = Geometry.Parse("M6,9 L12,15 L18,9"),
        ["book"] = Geometry.Parse("M5,4 L15,4 Q18,4 18,7 L18,20 L5,20 Z M8,8 L15,8 M8,12 L15,12"),
        ["github"] = Geometry.Parse("M21,12 A9,9 0 1 1 3,12 A9,9 0 1 1 21,12 M9,20 L9,16.5 Q6.5,17 5.5,14.5 M15,20 L15,16.5 Q17.5,15.5 17,11 Q17.5,8.5 16,7 Q14.5,7.5 12,7.5 Q9.5,7.5 8,7 Q6.5,8.5 7,11 Q6.5,15.5 9,16.5"),
        ["gitlab"] = Geometry.Parse("M4,13 L12,21 L20,13 L17,4 L14,13 L10,13 L7,4 Z"),
        ["discard"] = Geometry.Parse("M9,14 L4,9 L9,4 M4,9 L15,9 A5,5 0 0 1 15,19 L10,19"),
        ["tag"] = Geometry.Parse("M3,3 L12,3 L21,12 L12,21 L3,12 Z M9,7.5 A1.5,1.5 0 1 1 6,7.5 A1.5,1.5 0 1 1 9,7.5"),
        // Two cherries on stems from one point, the left one lower.
        ["cherrypick"] = Geometry.Parse("M10,17 A3,3 0 1 1 4,17 A3,3 0 1 1 10,17 M20,15 A3,3 0 1 1 14,15 A3,3 0 1 1 20,15 M7,14 Q8,7 14,4 M17,12 Q16,7 14,4 M14,4 Q18,3 20,5"),
        // Three dots in a row: more actions behind it.
        ["more"] = Geometry.Parse("M6.9,12 A0.9,0.9 0 1 1 5.1,12 A0.9,0.9 0 1 1 6.9,12 M12.9,12 A0.9,0.9 0 1 1 11.1,12 A0.9,0.9 0 1 1 12.9,12 M18.9,12 A0.9,0.9 0 1 1 17.1,12 A0.9,0.9 0 1 1 18.9,12"),
        // An arrow going back to a bar: a branch moved back to an earlier commit.
        ["reset"] = Geometry.Parse("M20,12 L8,12 M12,7 L7,12 L12,17 M4,6 L4,18"),
        // A prompt in a window: a terminal.
        ["terminal"] = Geometry.Parse("M3,5 L21,5 L21,19 L3,19 Z M7,9 L10,12 L7,15 M12,15 L17,15"),
        // A pencil: change what the row shows.
        ["edit"] = Geometry.Parse("M4,20 L4,16 L15,5 L19,9 L8,20 Z M13,7 L17,11"),
        ["stash"] = Geometry.Parse("M3,5 L21,5 L21,9 L3,9 Z M4,9 L4,20 L20,20 L20,9 M10,13 L14,13"),
        // Eight teeth 12 degrees wide on a 10 unit radius, roots on 7.4, a 3.4 hub: symmetric, so it reads as a gear at 18 px.
        ["settings"] = Geometry.Parse("M19.26,10.59 L21.95,10.95 A10,10 0 0 1 21.95,13.05 L19.26,13.41 A7.4,7.4 0 0 1 18.13,16.14 L19.77,18.29 A10,10 0 0 1 18.29,19.77 L16.14,18.13 A7.4,7.4 0 0 1 13.41,19.26 L13.05,21.95 A10,10 0 0 1 10.95,21.95 L10.59,19.26 A7.4,7.4 0 0 1 7.86,18.13 L5.71,19.77 A10,10 0 0 1 4.23,18.29 L5.87,16.14 A7.4,7.4 0 0 1 4.74,13.41 L2.05,13.05 A10,10 0 0 1 2.05,10.95 L4.74,10.59 A7.4,7.4 0 0 1 5.87,7.86 L4.23,5.71 A10,10 0 0 1 5.71,4.23 L7.86,5.87 A7.4,7.4 0 0 1 10.59,4.74 L10.95,2.05 A10,10 0 0 1 13.05,2.05 L13.41,4.74 A7.4,7.4 0 0 1 16.14,5.87 L18.29,4.23 A10,10 0 0 1 19.77,5.71 L18.13,7.86 A7.4,7.4 0 0 1 19.26,10.59 Z M15.4,12 A3.4,3.4 0 1 1 8.6,12 A3.4,3.4 0 1 1 15.4,12")
    };

    public static IReadOnlyCollection<string> Kinds => Shapes.Keys;
    public static Geometry Get(string kind) => Shapes.GetValueOrDefault(kind, Shapes["file"]);
}
