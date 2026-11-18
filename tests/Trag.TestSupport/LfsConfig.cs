namespace Trag.TestSupport;

// Git LFS as one repository sees it, whatever the machine has: installed is what git lfs install writes, missing turns
// the filter off as on a machine without Git LFS.
public static class LfsConfig
{
    public static async Task SetAsync(GitFixture f, string repository, bool installed)
    {
        await f.GitAtAsync(repository, "config", "filter.lfs.process", installed ? "git-lfs filter-process" : "");
        await f.GitAtAsync(repository, "config", "filter.lfs.clean", installed ? "git-lfs clean -- %f" : "");
        await f.GitAtAsync(repository, "config", "filter.lfs.smudge", installed ? "git-lfs smudge -- %f" : "");
        await f.GitAtAsync(repository, "config", "filter.lfs.required", installed ? "true" : "false");
    }
}
