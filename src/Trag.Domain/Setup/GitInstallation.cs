namespace Trag.Domain.Setup;

// A Git on this machine: its program, the version it answers with, and where it was found.
public sealed record GitInstallation(string Path, Version Version, GitOrigin Origin);
