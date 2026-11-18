using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace Trag.Desktop.Controls;

// A toolbar button: an icon tile above a caption. Fourteen of these exist and only the glyph, the caption, the command
// and the descriptions differ, so the body lives here once. It keeps Button as its style key, which is what makes the
// Button.tool rules in Themes/Toolbar.axaml apply to it unchanged - an Avalonia type selector matches the exact type.
public sealed class ToolButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    public static readonly StyledProperty<string> KindProperty = AvaloniaProperty.Register<ToolButton, string>(nameof(Kind), "file");
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<ToolButton, string>(nameof(Text), "");
    // The glyph is drawn in the accent colour instead of the text colour: publish, and open on the repository list.
    public static readonly StyledProperty<bool> AccentProperty = AvaloniaProperty.Register<ToolButton, bool>(nameof(Accent));
    // The tile gets an accent border too; only publish asks for it.
    public static readonly StyledProperty<bool> HighlightedProperty = AvaloniaProperty.Register<ToolButton, bool>(nameof(Highlighted));
    // While pull or push carries a count, the whole button turns accent: glyph, tile border and caption.
    public static readonly StyledProperty<bool> PendingProperty = AvaloniaProperty.Register<ToolButton, bool>(nameof(Pending));

    public string Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public bool Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public bool Highlighted { get => GetValue(HighlightedProperty); set => SetValue(HighlightedProperty, value); }
    public bool Pending { get => GetValue(PendingProperty); set => SetValue(PendingProperty, value); }

    private readonly LineIcon glyph = new() { Width = 19, Height = 19 };
    private readonly Border tile;
    private readonly TextBlock label = new() { HorizontalAlignment = HorizontalAlignment.Center };

    public ToolButton()
    {
        tile = new Border { Child = glyph };
        tile.Classes.Add("iconTile");
        Classes.Add("tool");
        Content = new StackPanel { Spacing = 4, Children = { tile, label } };
        ApplyGlyph();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KindProperty) glyph.Kind = Kind;
        else if (change.Property == TextProperty) label.Text = Text;
        else if (change.Property == PendingProperty) { label.Classes.Set("accent", Pending); ApplyTile(); ApplyGlyph(); }
        else if (change.Property == HighlightedProperty) ApplyTile();
        else if (change.Property == AccentProperty) ApplyGlyph();
    }

    private void ApplyTile() => tile.Classes.Set("highlighted", Highlighted || Pending);

    // The brush comes from the palette, so the glyph follows the theme the same way the hand-written markup did.
    private void ApplyGlyph()
    {
        glyph.Kind = Kind;
        glyph[!LineIcon.ForegroundProperty] = new DynamicResourceExtension(Accent || Pending ? "AccentBrush" : "TextBrush");
    }
}
