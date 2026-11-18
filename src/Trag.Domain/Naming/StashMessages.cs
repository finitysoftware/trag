namespace Trag.Domain.Naming;

// What a stash message has to be before it is offered to Git: bounded, printable, and not an option in disguise.
public static class StashMessages
{
    public static bool IsValid(string message) =>
        message.Length <= GitLimits.StashMessageLength && !message.Any(char.IsControl) && !message.TrimStart().StartsWith('-');
}
