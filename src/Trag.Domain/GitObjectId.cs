namespace Trag.Domain;

// One rule for a full object ID: 40 hex characters for SHA-1, 64 for SHA-256. Callers keep their own message, because
// an invalid ID means something different to a parser, to a reader and to a command that refuses to run. Short is the
// prefix the UI shows: seven characters where Git would, ten in the history list.
public static class GitObjectId
{
    public static bool IsValid(string value) => value.Length is 40 or 64 && value.All(char.IsAsciiHexDigit);

    public static bool IsValid(ReadOnlySpan<byte> value)
    {
        if (value.Length is not (40 or 64)) return false;
        foreach (var b in value)
            if (!char.IsAsciiHexDigit((char)b)) return false;
        return true;
    }

    // A commit as a person or a rebase todo writes it: at least seven hex characters, at most a full SHA-256 id.
    public static bool IsAbbreviation(string value) => value.Length is >= 7 and <= 64 && value.All(char.IsAsciiHexDigit);

    public static void Validate(string value)
    {
        if (!IsValid(value)) throw new FormatException("Invalid object ID.");
    }

    // Git's id for no object at all, and for a line no commit holds yet: every digit a zero.
    public static bool IsZero(string id) => id.All(digit => digit == '0');
    public static string Short(string id, int length = 7) => id[..Math.Min(length, id.Length)];
}
