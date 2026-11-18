namespace Trag.Domain.Errors;

// A file the blame view cannot show line by line, with the sentence it shows instead.
public sealed class BlameException(string message) : Exception(message);
