namespace Trag.Domain.Presentation;

// How much highlighting is left for one document, a diff or a blamed file: the characters scanned and the tokens found
// so far against the limits. Once a line would go beyond them, the rest of the document stays plain.
public struct SyntaxBudget
{
    public const int CharacterLimit = 1_000_000;
    public const int TokenLimit = 65_536;

    private int characters;
    private int tokens;
    public readonly bool Covers(int size) => characters + size <= CharacterLimit && tokens + size <= TokenLimit;
    public void Spend(int scanned, int found)
    {
        characters += scanned;
        tokens += found;
    }
}
