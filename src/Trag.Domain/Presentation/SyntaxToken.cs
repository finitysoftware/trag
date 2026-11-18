namespace Trag.Domain.Presentation;

public readonly record struct SyntaxToken(int Start, int Length, SyntaxKind Kind);
