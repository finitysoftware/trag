using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Trag.Desktop.Themes;
using Trag.Domain.Presentation;

namespace Trag.Desktop.Controls;

// A line of code: its text in runs, the spans of its syntax tokens in the colors the diff uses for the current theme and
// the text between them in the text block's own color. The runs are made again when the code, its tokens or the theme
// change; a recycled row gets its new code before its new tokens, so a token that does not fit the code is left out.
public sealed class CodeTextBlock : TextBlock
{
    public static readonly StyledProperty<string?> CodeProperty = AvaloniaProperty.Register<CodeTextBlock, string?>(nameof(Code));
    public static readonly StyledProperty<IReadOnlyList<SyntaxToken>?> TokensProperty = AvaloniaProperty.Register<CodeTextBlock, IReadOnlyList<SyntaxToken>?>(nameof(Tokens));

    public string? Code { get => GetValue(CodeProperty); set => SetValue(CodeProperty, value); }
    public IReadOnlyList<SyntaxToken>? Tokens { get => GetValue(TokensProperty); set => SetValue(TokensProperty, value); }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CodeProperty || change.Property == TokensProperty || change.Property == ThemeVariantScope.ActualThemeVariantProperty) Color();
    }

    private void Color()
    {
        var code = Code ?? "";
        var colors = SyntaxColors.For(AppThemes.IsDark(ActualThemeVariant));
        var runs = new InlineCollection();
        var at = 0;
        foreach (var token in (Tokens ?? []).Where(token => token.Start >= at && token.Start + token.Length <= code.Length))
        {
            if (token.Start > at) runs.Add(new Run(code[at..token.Start]));
            runs.Add(new Run(code.Substring(token.Start, token.Length)) { Foreground = colors[(int)token.Kind] });
            at = token.Start + token.Length;
        }
        if (at < code.Length) runs.Add(new Run(code[at..]));
        Inlines = runs;
    }
}
