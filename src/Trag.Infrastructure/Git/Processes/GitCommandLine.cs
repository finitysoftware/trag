using System.Diagnostics;

using Trag.Infrastructure.Accounts.Credentials;

namespace Trag.Infrastructure.Git.Processes;

internal sealed class GitCommandLine(GitExecutable executable)
{
    public string Program => executable.Path;

    public ProcessStartInfo Create(string repository, IEnumerable<string> arguments, IReadOnlyDictionary<string, string?>? environment, bool literalPathspecs = true)
    {
        var start = new ProcessStartInfo(executable.Path)
        {
            WorkingDirectory = repository,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new System.Text.UTF8Encoding(false),
            StandardInputEncoding = new System.Text.UTF8Encoding(false),
            StandardErrorEncoding = new System.Text.UTF8Encoding(false)
        };
        start.ArgumentList.Add("--no-pager");
        // Git for Windows reaches a path past MAX_PATH only under core.longpaths: without it a tracked file there reads as
        // deleted and stages as a deletion, with only a warning. Given on the command line, the setting is above any
        // configuration, so a repository's own false is overridden too.
        if (OperatingSystem.IsWindows())
        {
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("core.longpaths=true");
        }
        // Trag gives Git every message in UTF-8 and reads what Git writes for it as UTF-8. Git labels a commit with the
        // encoding i18n.commitEncoding names and writes the message files of a rebase, a cherry-pick or a revert, and a
        // fold's, in it, so under another one a commit would carry UTF-8 bytes labelled as that one and a stop's message
        // would read as other letters. Given here, every Git Trag starts agrees on UTF-8, as does the Git of an exec line
        // or a hook, which Git hands it on to. A commit labelled with another encoding is turned into UTF-8 when Git
        // replays it, if Git can read it in the encoding it names; a message it cannot read so, and bytes that are not
        // UTF-8 under no label, are committed with those bytes read as Latin-1, as Git does by default.
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("i18n.commitEncoding=UTF-8");
        // Literal pathspecs keep user paths from acting as globs. The commands that misbehave under them run without:
        // "git stash push --include-untracked" (Git leaves the stashed untracked files in place; Trag passes it no
        // pathspecs), check-ignore, which takes path names, the match of a configuration's submodule pathspecs, and the
        // status of the paths the watcher signaled, which names each with literal and icase magic.
        // Git hands the setting on to every hook it runs, so the commands that run the user's hooks and name no paths
        // run without it too: commit, switch, merge, rebase, cherry-pick and revert, and every transfer; a hook's own
        // Git then reads pathspec magic as the user's does.
        if (literalPathspecs) start.ArgumentList.Add("--literal-pathspecs");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        // The chosen repository must not be redirected by the launching terminal, nor its paths read with the pathspec
        // magic a terminal sets for every command: Git refuses that beside --literal-pathspecs, and check-ignore refuses
        // literal magic, so either would stop every command that names a path. Nor may the -c and --config-env values of
        // the one Git command that ran Trag as an alias or a hook rule over the local configuration of every repository
        // Trag opens; GIT_CONFIG_COUNT and its keys are set by the user on purpose and stay. Nor may that Git's exec path
        // make the Git Trag chose run another installation's helpers, nor GIT_CONFIG point Git's config command at
        // another file than the repository's configuration, which also fails every --local read and write.
        foreach (var key in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR", "GIT_INDEX_FILE", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_NAMESPACE", "GIT_PREFIX",
            "GIT_LITERAL_PATHSPECS", "GIT_GLOB_PATHSPECS", "GIT_NOGLOB_PATHSPECS", "GIT_ICASE_PATHSPECS", "GIT_CONFIG_PARAMETERS", "GIT_EXEC_PATH", "GIT_CONFIG" })
            start.Environment.Remove(key);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        // Windows' own ssh-keygen asks for a key's passphrase on the console, which without a window nobody can answer, so
        // a commit signed with such a key would wait for ever. An askpass that cannot start makes it give up at once, as
        // Git's own ssh-keygen does without a terminal; a key ssh-agent holds asks for nothing.
        if (OperatingSystem.IsWindows())
        {
            start.Environment["SSH_ASKPASS_REQUIRE"] = "force";
            start.Environment["SSH_ASKPASS"] = "NUL";
        }
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        start.Environment["LC_ALL"] = "C";
        if (environment is not null)
            foreach (var entry in environment) start.Environment[entry.Key] = entry.Value;
        if (environment?.ContainsKey(AskPassClient.MarkerVariable) == true)
        {
            foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_TRACE", StringComparison.OrdinalIgnoreCase)).ToArray())
                start.Environment.Remove(key);
        }
        return start;
    }
}
