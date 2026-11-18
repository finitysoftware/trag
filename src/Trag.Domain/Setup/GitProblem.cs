namespace Trag.Domain.Setup;

// Why there is no Git to run: none was found, the one found is older than Trag works with, or the chosen file is no Git.
public enum GitProblem { None, Missing, TooOld, NotGit }
