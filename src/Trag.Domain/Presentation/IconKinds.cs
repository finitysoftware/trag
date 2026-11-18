using Trag.Domain.Accounts;

namespace Trag.Domain.Presentation;

// The line icon a row or a dialog shows, by the key the icon set knows it by.
public static class IconKinds
{
    public const string Merge = "merge";
    public const string Rebase = "rebase";
    public static string Account(AccountProvider provider) => provider == AccountProvider.GitHub ? "github" : "gitlab";
    public static string RemoteRepository(bool cloned) => cloned ? "folder" : "book";
}
