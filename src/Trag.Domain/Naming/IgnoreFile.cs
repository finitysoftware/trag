using System.Text;

namespace Trag.Domain.Naming;

// A line added to a .gitignore as it is: the bytes there stay, the line goes after them on a line of its own, ended as
// the file ends its lines, and a line the file already has is not written again, a byte order mark before the first
// aside. Null when there is nothing to add.
public static class IgnoreFile
{
    public static string? Addition(byte[] existing, string line)
    {
        var text = Encoding.UTF8.GetString(existing).TrimStart('﻿');
        if (text.Split('\n').Any(present => present.TrimEnd('\r') == line)) return null;
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var start = existing.Length == 0 || existing[^1] == (byte)'\n' ? "" : newline;
        return start + line + newline;
    }
}
