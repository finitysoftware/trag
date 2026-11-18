using Trag.Domain.Presentation.Text;

namespace Trag.Domain.Merge;

// One side of a conflict as the user sees it: the word a sentence uses for it, which in capitals labels its card, the
// name its card shows, the shorter name a sentence uses, and the side Git calls it (ours or theirs).
public sealed record ConflictSide(string Word, string Name, string Short, ConflictChoice Git)
{
    public string Label => Word.ToUpperInvariant();
    public string Title => MergeText.SideTitle(Label, Name);
    // A version already in HEAD is kept, the other one is taken.
    public string Verb => MergeText.SideVerb(Git == ConflictChoice.Mine);
    public string UseAction => MergeText.UseSide(Word);
    public string UseTip => MergeText.UseSideTip(Word);
    public string TakeAction => MergeText.TakeSide(Word);
}
