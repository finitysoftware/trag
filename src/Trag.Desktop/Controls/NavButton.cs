using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Trag.Domain.Presentation;

namespace Trag.Desktop.Controls;

// A section button in the sidebar: an 18 px accent glyph beside a label, in one row. There is one per section, and only
// the glyph, the label, the section and the command differ. Like ToolButton it keeps Button as its style key, so the
// Button.nav rules in Themes/Toolbar.axaml apply to it unchanged.
public sealed class NavButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    public static readonly StyledProperty<string> KindProperty = AvaloniaProperty.Register<NavButton, string>(nameof(Kind), "file");
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<NavButton, string>(nameof(Text), "");
    // The section this button opens and the one the workspace shows: the button is drawn as selected while they match.
    public static readonly StyledProperty<WorkspaceSection> SectionProperty = AvaloniaProperty.Register<NavButton, WorkspaceSection>(nameof(Section));
    public static readonly StyledProperty<WorkspaceSection> CurrentProperty = AvaloniaProperty.Register<NavButton, WorkspaceSection>(nameof(Current));

    public string Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public WorkspaceSection Section { get => GetValue(SectionProperty); set => SetValue(SectionProperty, value); }
    public WorkspaceSection Current { get => GetValue(CurrentProperty); set => SetValue(CurrentProperty, value); }
    public bool IsActive => Section == Current;

    private readonly LineIcon glyph = new() { Width = 18, Height = 18 };
    private readonly TextBlock label = new() { VerticalAlignment = VerticalAlignment.Center };

    public NavButton()
    {
        glyph[!LineIcon.ForegroundProperty] = new DynamicResourceExtension("AccentBrush");
        Classes.Add("nav");
        // The style stretches the button and its content; the sections line their glyphs up on the left instead.
        HorizontalContentAlignment = HorizontalAlignment.Left;
        Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { glyph, label } };
        this[!AutomationProperties.NameProperty] = this[!TextProperty];
        Classes.Set("active", IsActive);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KindProperty) glyph.Kind = Kind;
        else if (change.Property == TextProperty) label.Text = Text;
        else if (change.Property == SectionProperty || change.Property == CurrentProperty) Classes.Set("active", IsActive);
    }
}
