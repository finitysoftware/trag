namespace Trag.Domain.Merge;

// How a file of the merge got its content: Git merged it on its own, or a conflict was resolved with our side, their
// side, or something else the user wrote.
public enum Resolution { Automatic, Mine, Theirs, Edited }
