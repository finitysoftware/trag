namespace Trag.Application.Updates;

// Where the update check is: not asked yet, asking, or what it learned.
public enum UpdateState { NotChecked, Checking, UpToDate, Available, Failed }
