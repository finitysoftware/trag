using System.Text;
using System.Text.RegularExpressions;
using Trag.Domain.Presentation.Text;

namespace Trag.Infrastructure.Git.Diagnostics;

// Everything Git prints passes through here before a screen shows it or a log keeps it: a clone, a write, progress and
// the addresses inside. Progress lines are collapsed, addresses and secrets hidden, control characters dropped, and the
// text kept to a bound that keeps Git's error at the end.
internal static partial class GitOutputRedaction
{
    private const int Limit = 8192;
    // Git errors sit at the end of its output, after progress and hook chatter, so truncation keeps the tail as well as the head.
    // The first cut bounds the work of hiding; where a part holds a line break it keeps whole lines, because a secret it ran
    // through would no longer be recognised, and hiding a long secret can bring the cut into the text shown.
    public static string Redact(string text)
    {
        text = CollapseProgress(text);
        if (text.Length > 4 * Limit)
        {
            var end = text[^(3 * Limit)..];
            var cut = text.LastIndexOf('\n', Limit);
            text = (cut < 0 ? text[..Limit] : text[..(cut + 1)]) + "\n" + end[(end.IndexOf('\n') + 1)..];
        }
        text = Hide(text);
        text = new string(text.Where(c => !char.IsControl(c) || c is '\n' or '\r' or '\t').ToArray());
        if (text.Length <= Limit) return text;
        const int head = 2048, tail = 6000;
        return text[..head] + "\n" + DetailsText.Omitted(text.Length - head - tail) + "\n" + text[^tail..];
    }
    // Addresses keep no credentials and secrets are replaced; nothing else changes, for text that is kept whole.
    public static string Hide(string text) =>
        Secrets().Replace(ScpCredentials().Replace(Addresses().Replace(text, match => AddressRedactor.Redact(match.Value)), ""), DetailsText.Redacted);

    // Progress lines are rewritten in place with '\r'; keep what a terminal would show, the last state of each line.
    private static string CollapseProgress(string text)
    {
        if (!text.Contains('\r')) return text;
        var builder = new StringBuilder(text.Length);
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            var last = trimmed.LastIndexOf('\r');
            builder.Append(last >= 0 ? trimmed[(last + 1)..] : trimmed).Append('\n');
        }
        if (builder.Length > 0) builder.Length--;
        return builder.ToString();
    }
    // An address ends before whitespace, brackets and quotes, and keeps no trailing punctuation: Git quotes the address
    // it could not use and ends the sentence after it, and neither the quote nor the colon is part of the address.
    [GeneratedRegex("[a-zA-Z][a-zA-Z0-9+.-]*://[^\\s<>'\"]*[^\\s<>'\".,;:!?)]", RegexOptions.CultureInvariant)]
    private static partial Regex Addresses();
    // An scp-like address, user:password@host:path, has no scheme for Addresses to find: its user and password go.
    [GeneratedRegex(@"[^\s'""<>@/:]+:[^\s'""<>@/]+@(?=[A-Za-z0-9.-]+:)", RegexOptions.CultureInvariant)]
    private static partial Regex ScpCredentials();
    [GeneratedRegex(@"(?im)\b(?:authorization|password|access[_-]?token|token|secret)\s*[:=][^\r\n]*|\b(?:gh[pousr]_|github_pat_|glpat-)[A-Za-z0-9_-]+", RegexOptions.CultureInvariant)]
    private static partial Regex Secrets();
}
