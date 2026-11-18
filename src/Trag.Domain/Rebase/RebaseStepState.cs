namespace Trag.Domain.Rebase;

// Where one commit of a rebase's plan is: replayed already, the one Git stopped at, or still waiting.
public enum RebaseStepState { Applied, Current, Waiting }
