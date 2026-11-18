namespace Trag.Domain.Accounts;

// Which identity Git uses for one remote: the choices and the saved one. One matching Trag account is chosen
// automatically; the choice is offered once two accounts compete for the remote, or when the repository remembers Git's
// own credentials although a Trag account could serve the remote, so the way back to that account never needs Git's
// configuration edited by hand. A choice is confirmed only when it is the saved one.
public sealed record RemoteAccountState(IReadOnlyList<AccountChoice> Choices, string? SelectedId, string Message)
{
    public bool HasChoices => Accounts > 1 || SelectedId == AccountChoice.External && Accounts == 1;
    private int Accounts => Choices.Count(choice => choice.Id != AccountChoice.External);
    public AccountChoice? Saved => Choices.FirstOrDefault(choice => choice.Id == SelectedId);
    public bool IsUnsaved(string? selectedId) => selectedId is not null && selectedId != SelectedId;
    public bool IsConfirmed(string? selectedId) => selectedId is not null && selectedId == SelectedId;
}
