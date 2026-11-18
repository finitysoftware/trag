using Trag.Domain.Rebase;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Rebase;

// The plan Trag hands Git for a rewrite: one line per commit in the chosen order, a squash or fixup folded into the commit
// before it, a drop said out loud, and the message a commit gets set by an exec line that reads it from a file, so no
// editor ever opens and a stop on the way changes nothing.
public sealed class RewriteTodoTests
{
    // Current Git writes "pick <id> # <subject>", older Git "pick <id> <subject>"; a fixup may carry an option first, and
    // a command without a commit, a blank line or a comment is no commit of the plan.
    [Fact]
    public void WhenGitsPlanIsRead_ShouldListItsCommitsWithTheirCommandsAndSubjects()
    {
        var plan = RewriteTodo.Parse("pick 1a2b3c4 # Add login\r\nsquash 5d6e7f8 Fix typo\nfixup -C 9a8b7c6 # Reword\nexec make\n\n# a comment\nbreak\n");
        Assert.Equal([("pick", "1a2b3c4", "Add login"), ("squash", "5d6e7f8", "Fix typo"), ("fixup", "9a8b7c6", "Reword")],
            plan.Select(commit => (commit.Action, commit.Id, commit.Subject)));
        Assert.Empty(RewriteTodo.Parse(null));
    }

    // rebase.abbreviateCommands writes one letter for each command, rebase.instructionFormat any subject it likes, and a
    // subject may start with the comment character: the commits read the same.
    [Fact]
    public void WhenGitAbbreviatesCommandsOrFormatsTheSubject_ShouldStillReadTheCommits()
    {
        var plan = RewriteTodo.Parse("p 1a2b3c4 Ana: Add login\nf 5d6e7f8 # Fix typo\nd 9a8b7c6 # #123 Old work\n");
        Assert.Equal([("p", "1a2b3c4", "Ana: Add login"), ("f", "5d6e7f8", "Fix typo"), ("d", "9a8b7c6", "#123 Old work")],
            plan.Select(commit => (commit.Action, commit.Id, commit.Subject)));
    }

    // Git folds a commit it put back again from the plan's first line that names a commit to replay, after the exec, break,
    // label and drop lines a plan edited in a terminal may put before it. A squash adds its message to the fold's a second
    // time, and so does a fixup -C or -c once a squash is in the fold, as Git 2.43, 2.50 and 2.55 do; current-fixups lists
    // the folds so far by command alone, and the plan has "s", "f" or "d" once it is edited under rebase.abbreviateCommands.
    [Theory]
    [InlineData("squash 5d6e7f8 # Fix typo\npick 9a8b7c6 # Next\n", "squash 5d6e7f8\n", true)]
    [InlineData("fixup -C 5d6e7f8 # Reword\n", "squash 1a2b3c4\nfixup 5d6e7f8\n", true)]
    [InlineData("f -c 5d6e7f8 # Reword\n", "squash 1a2b3c4\nfixup 5d6e7f8\n", true)]
    [InlineData("s 5d6e7f8 # Fix typo\n", "squash 5d6e7f8\n", true)]
    [InlineData("exec make\nsquash 5d6e7f8 # Fix typo\n", "squash 5d6e7f8\n", true)]
    [InlineData("label keep\nbreak\nfixup -C 5d6e7f8 # Reword\n", "squash 1a2b3c4\nfixup 5d6e7f8\n", true)]
    [InlineData("d 9a8b7c6 # Next\ns 5d6e7f8 # Fix typo\n", "squash 5d6e7f8\n", true)]
    [InlineData("fixup -C 5d6e7f8 # Reword\n", "fixup 5d6e7f8\n", false)]
    [InlineData("fixup 5d6e7f8 # Fix typo\n", "squash 1a2b3c4\nfixup 5d6e7f8\n", false)]
    public void WhenGitPutBackAFold_ShouldSayWhetherFoldingItAgainRepeatsItsMessage(string todo, string fixups, bool repeats) =>
        Assert.Equal(repeats, RewriteTodo.RepeatsMessage(todo, fixups));

    // The fold Git put back is the commit the last line of current-fixups names in full, written before Git tried to commit
    // it. A plan changed in a terminal with git rebase --edit-todo may no longer fold that commit first: the user dropped
    // it, made it a pick or removed its line; a drop of another commit above it changes nothing. A plan edited by hand may
    // give the commit's id short.
    [Theory]
    [InlineData("exec make\nsquash 5d6e7f8 # Fix typo\nsquash 9a8b7c6 # Next\n", "squash 5d6e7f8a9b0c1d2e3f4a5b6c7d8e9f0a1b2c3d4e\n", true)]
    [InlineData("f -C 5d6e7f8a9b0c1d2e3f4a5b6c7d8e9f0a1b2c3d4e # Reword\n", "squash 1a2b3c4\nfixup 5d6e7f8a9b0c1d2e3f4a5b6c7d8e9f0a1b2c3d4e\n", true)]
    [InlineData("drop 9a8b7c6 # Next\nfixup 5d6e7f8 # Fix typo\n", "fixup 5d6e7f8a9b0c1d2e3f4a5b6c7d8e9f0a1b2c3d4e\n", true)]
    [InlineData("drop 5d6e7f8 # Fix typo\nsquash 9a8b7c6 # Next\n", "squash 5d6e7f8a9b0c1d2e3f4a5b6c7d8e9f0a1b2c3d4e\n", false)]
    [InlineData("pick 5d6e7f8 # Fix typo\nsquash 9a8b7c6 # Next\n", "squash 5d6e7f8a9b0c1d2e3f4a5b6c7d8e9f0a1b2c3d4e\n", false)]
    [InlineData("squash 9a8b7c6 # Next\n", "squash 5d6e7f8a9b0c1d2e3f4a5b6c7d8e9f0a1b2c3d4e\n", false)]
    public void WhenGitPutBackAFold_ShouldSayWhetherThePlanStillFoldsThatCommitFirst(string todo, string fixups, bool folds) =>
        Assert.Equal(folds, RewriteTodo.FoldsAgain(todo, fixups));

    // A commit may have no message. Trag's rewrite, as current Git does, writes its line as "pick <id> # ", and once the
    // sequence editor is done Git saves the plan in done and git-rebase-todo without the trailing space.
    [Fact]
    public void WhenGitSavesTheLineOfACommitWithoutAMessage_ShouldReadItWithAnEmptySubject() =>
        Assert.Equal([("pick", Snapshots.Id('b'), "")],
            RewriteTodo.Parse("pick " + Snapshots.Id('b') + " #\n").Select(commit => (commit.Action, commit.Id, commit.Subject)));

    // A plan edited in a terminal (git rebase --edit-todo) may name a commit with no subject after it.
    [Fact]
    public void WhenALineNamesItsCommitAlone_ShouldReadItWithAnEmptySubject() =>
        Assert.Equal([("pick", "1a2b3c4", "")], RewriteTodo.Parse("pick 1a2b3c4\n").Select(commit => (commit.Action, commit.Id, commit.Subject)));

    // A plan edited in a terminal may hold blank lines and comments between its commits, and a commit line commented out
    // with no space after the comment character.
    [Fact]
    public void WhenThePlanHasCommentsBetweenItsCommits_ShouldReadTheCommitsAfterThemAndNoneCommentedOut() =>
        Assert.Equal([("pick", "1a2b3c4"), ("pick", "9a8b7c6")],
            RewriteTodo.Parse("pick 1a2b3c4 # Add login\n\n#pick 5d6e7f8 # Fix typo\npick 9a8b7c6 # Next\n").Select(commit => (commit.Action, commit.Id)));

    private const string Directory = "D:/Repos/my repo/.git/trag-rebase";

    private static RewriteStep Step(char id, RewriteAction action = RewriteAction.Pick, string? message = null) =>
        new(Snapshots.Id(id), "Subject " + id, "Message " + id, action, message ?? "Message " + id);

    private static string Pick(char id) => "pick " + Snapshots.Id(id) + " # Subject " + id;
    private static string Amend(int file) => "exec git -c i18n.commitEncoding=UTF-8 commit --amend --allow-empty --no-verify --cleanup=verbatim -F '" + Directory + "/" + file + ".txt'";

    [Fact]
    public void WhenCommitsAreOnlyReordered_ShouldPickThemInTheNewOrderWithoutMessages()
    {
        var script = RewriteTodo.For([Step('c'), Step('b')], Directory);

        Assert.Equal(Pick('c') + "\n" + Pick('b') + "\n", script.Todo);
        Assert.Empty(script.Messages);
    }

    [Fact]
    public void WhenACommitIsDropped_ShouldSayDropForIt() =>
        Assert.Equal(Pick('b') + "\ndrop " + Snapshots.Id('c') + " # Subject c\n", RewriteTodo.For([Step('b'), Step('c', RewriteAction.Drop)], Directory).Todo);

    [Fact]
    public void WhenACommitIsReworded_ShouldSetItsNewMessageRightAfterIt()
    {
        var script = RewriteTodo.For([Step('b', RewriteAction.Reword, "New b"), Step('c')], Directory);

        Assert.Equal(Pick('b') + "\n" + Amend(1) + "\n" + Pick('c') + "\n", script.Todo);
        Assert.Equal(["New b"], script.Messages);
    }

    // A reword left with its message is a plain pick.
    [Fact]
    public void WhenARewordKeepsItsMessage_ShouldNotSetIt() =>
        Assert.Equal(Pick('b') + "\n", RewriteTodo.For([Step('b', RewriteAction.Reword)], Directory).Todo);

    // Squash and fixup both fold into the commit before them; the group's message is set once, after the last of them,
    // and only when there is a squash or the first commit's message changed.
    [Fact]
    public void WhenCommitsAreFoldedIntoOne_ShouldFixThemUpAndSetTheGroupsMessageAfterTheLast()
    {
        var script = RewriteTodo.For(
            [Step('b', message: "Joined"), Step('c', RewriteAction.Squash), Step('d', RewriteAction.Drop), Step('e', RewriteAction.Fixup), Step('f'), Step('g', RewriteAction.Fixup)],
            Directory);

        Assert.Equal(string.Join("\n",
            Pick('b'), "fixup " + Snapshots.Id('c') + " # Subject c", "drop " + Snapshots.Id('d') + " # Subject d", "fixup " + Snapshots.Id('e') + " # Subject e",
            Amend(1), Pick('f'), "fixup " + Snapshots.Id('g') + " # Subject g") + "\n", script.Todo);
        Assert.Equal(["Joined"], script.Messages);
    }

    // The file's path goes to sh: Windows separators become slashes, and a quote in it is closed, escaped and reopened.
    [Fact]
    public void WhenTheMessagesFolderHasAQuoteOrBackslashes_ShouldQuoteItForTheShell()
    {
        var script = RewriteTodo.For([Step('b', RewriteAction.Reword, "New b")], @"D:\Repos\Ana's\.git\trag-rebase");

        Assert.Contains("-F 'D:/Repos/Ana'\\''s/.git/trag-rebase/1.txt'", script.Todo);
    }

    // Git hands the sequence editor the path of its plan; Trag's plan is copied over it, the path quoted for sh.
    [Fact]
    public void WhenGitAsksForThePlan_ShouldCopyTragsPlanOverIt() =>
        Assert.Equal("cp 'D:/Repos/Ana'\\''s/.git/trag-rebase/todo'", RewriteTodo.Editor(@"D:\Repos\Ana's\.git\trag-rebase\todo"));

    // A commit's subject is anyone's text: it stays a comment on its own line and cannot add a command to the plan.
    [Fact]
    public void WhenASubjectHoldsALineBreak_ShouldKeepItOnTheCommitsLine()
    {
        var step = new RewriteStep(Snapshots.Id('b'), "Fine\nexec rm -rf .\r", "Fine", RewriteAction.Pick, "Fine");

        Assert.Equal("pick " + Snapshots.Id('b') + " # Fine exec rm -rf . \n", RewriteTodo.For([step], Directory).Todo);
    }
}
