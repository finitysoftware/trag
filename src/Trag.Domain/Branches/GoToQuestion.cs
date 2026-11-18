namespace Trag.Domain.Branches;

// What going to a commit asks the user: the branch whose tip it is, to switch there, or else a new branch there under the
// suggested name, or a detached look.
public sealed record GoToQuestion(BranchInfo? Tip, string SuggestedName);
