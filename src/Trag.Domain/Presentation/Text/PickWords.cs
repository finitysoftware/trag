using Trag.Domain.Status;

namespace Trag.Domain.Presentation.Text;

// The words of the view of a commit Git applies and stopped at its conflicts, each naming the operation its way
// (PickText): the title, Finish, and the question before it is aborted.
public sealed record PickWords(RepositoryOperation Operation)
{
    public string Title => PickText.InProgress(Operation);
    public string FinishAction => PickText.FinishAction(Operation);
    public string FinishHint => PickText.FinishHint(Operation);
    public string AbortAction => PickText.AbortAction(Operation);
    public string AbortTitle => PickText.AbortTitle(Operation);
    public string AbortQuestion => PickText.AbortQuestion(Operation);
    public string KeepAction => PickText.KeepAction(Operation);
    public string ConfirmAbortAction => PickText.ConfirmAbortAction(Operation);
}
