namespace Trag.Domain.Presentation.Text;

// What Trag writes into Git's output before the Details pane shows it: what it hid, and how much it left out.
public static class DetailsText
{
    public const string Redacted = "[redacted]";
    public const string HiddenAddress = "Address hidden (invalid URL)";
    public static string Omitted(int characters) => "[… " + Counted.Number(characters) + " characters omitted …]";
}
