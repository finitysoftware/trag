namespace Trag.Domain.Naming;

public static class PublishNames
{
    public static readonly string Help = "Use letters, digits, '.', '_' or '-' (up to " + GitLimits.PublishNameLength + " characters).";
    public static string Error(string name)
    {
        var value = name.Trim();
        if (value.Length == 0) return "";
        if (value.Length > GitLimits.PublishNameLength || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-'))) return Help;
        if (value.StartsWith('.') || value.StartsWith('-') || value.EndsWith('.') || value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            return "The name cannot start with '.' or '-', or end with '.' or '.git'.";
        return "";
    }
    public static bool IsValid(string name) => name.Trim().Length != 0 && Error(name).Length == 0;
    public static string Suggest(string folderName)
    {
        var chars = folderName.Trim().Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-').ToArray();
        var value = new string(chars).Trim('.', '-');
        while (value.Contains("--", StringComparison.Ordinal)) value = value.Replace("--", "-", StringComparison.Ordinal);
        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) value = value[..^4].TrimEnd('.', '-');
        if (value.Length > GitLimits.PublishNameLength) value = value[..GitLimits.PublishNameLength].TrimEnd('.', '-');
        return value.Length == 0 ? "repository" : value;
    }
}
