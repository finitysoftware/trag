namespace Trag.Domain.Presentation;

// The theme the user chose, in the order Settings lists them: the saved layout keeps its number. Settings shows them as
// System theme, White, Espresso, Graphite, Obsidian and Vantablack. Espresso is the warm dark theme Trag had first, at its
// old place; Graphite is the dark theme a dark Windows gives the system choice; Obsidian is near-black, Vantablack black.
public enum ThemeChoice { System, Light, DarkEspresso, DarkGraphite, DarkObsidian, DarkVantablack }
