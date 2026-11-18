namespace Trag.Domain.Merge;

// How a decided file is shown: conflict markers left in it come before anything else, then the side its resolution
// took (the user's own or the other), then content the user wrote, else Git merged it on its own.
public enum ResolvedKind { Markers, Yours, Other, Edited, Automatic }
