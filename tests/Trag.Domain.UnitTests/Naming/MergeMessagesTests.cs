using Trag.Domain.Naming;
using Trag.Domain.Status;

namespace Trag.Domain.UnitTests.Naming;

// The incoming side of a merge is named the way Git's first line of MERGE_MSG names it: a branch, a branch pulled from
// an address, or a tag; a commit merged by its id keeps the short id.
public sealed class MergeMessagesTests
{
    private static RepositoryMerge Merge(string message) => new(new string('a', 40), new string('b', 40), message, "fingerprint", "");

    [Theory]
    [InlineData("Merge branch 'trunk' of https://example.invalid/team/project into main", "trunk")]
    [InlineData("Merge remote-tracking branch 'origin/trunk' into main", "origin/trunk")]
    [InlineData("Merge tag 'v1.2'", "v1.2")]
    [InlineData("Merge commit 'abc1234'", "aaaaaaa")]
    public void WhenMergeMsgNamesWhatIsMerged_ShouldNameTheIncomingSideTheSame(string message, string expected) =>
        Assert.Equal(expected, MergeMessages.IncomingName(Merge(message)));

    // At a stop on a fold's conflict the message is Git's editor text: its own lines and the messages it skips start with
    // core.commentChar, and Git commits the rest, the blank lines between run together.
    [Fact]
    public void WhenTheMessageIsGitsTextForAFold_ShouldKeepTheLinesGitCommits()
    {
        Assert.Equal("Add y", MergeMessages.Clean("# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nAdd y\n\n"
            + "# The commit message #2 will be skipped:\n\n# Make C\n"));
        Assert.Equal("Add b\n\n#12 Add c", MergeMessages.Clean("; This is a combination of 2 commits.\n; This is the 1st commit message:\n\nAdd b\n\n"
            + "; This is the commit message #2:\n\n#12 Add c\n"));
    }

    // Git keeps the messages it folds as they are, so a line of one that starts with the comment character is the message's
    // own; only the lines Git writes around them and the messages it skips go.
    [Fact]
    public void WhenAMessageGitKeepsInAFoldHasALineStartingWithTheCommentCharacter_ShouldKeepThatLine() =>
        Assert.Equal("Add y\n\n#12 closes the issue", MergeMessages.Clean("# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n"
            + "Add y\n\n#12 closes the issue\n\n# The commit message #2 will be skipped:\n\n# Make C\n"));

    // fixup -C takes the later commit's message and skips the first, as autosquash does for an amend! commit; a line of the
    // kept message that only starts like Git's heading stays.
    [Fact]
    public void WhenGitsTextForAFoldSkipsTheFirstMessage_ShouldKeepTheLaterOne() =>
        Assert.Equal("three subject\n\n# The body stays", MergeMessages.Clean("# This is a combination of 2 commits.\n# The 1st commit message will be skipped:\n\n"
            + "# other\n\n# This is the commit message #2:\n\nthree subject\n\n# The body stays\n"));

    // Autosquash's amend! commit: Git keeps its message but comments out the subject paragraph that names the commit it
    // amends, as Git 2.43 and 2.50 write it at a conflict, and commits the rest.
    [Fact]
    public void WhenAKeptMessageStartsWithAnAmendSubjectGitCommentedOut_ShouldLeaveThatParagraphOut() =>
        Assert.Equal("Better A\n\nbody of better A", MergeMessages.Clean("# This is a combination of 2 commits.\n# The 1st commit message will be skipped:\n\n"
            + "# Make A\n\n# This is the commit message #2:\n\n# amend! Make A\n\nBetter A\n\nbody of better A\n"));

    [Theory]
    [InlineData("squash!")]
    [InlineData("fixup!")]
    public void WhenAKeptMessageOfASquashStartsWithAMarkedSubjectGitCommentedOut_ShouldLeaveThatParagraphOut(string mark) =>
        Assert.Equal("Make A\n\nMore detail", MergeMessages.Clean("# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nMake A\n\n"
            + "# This is the commit message #2:\n\n# " + mark + " Make A\n\nMore detail\n"));

    // Git numbers its headings in order and writes each in one exact form, as Git 2.43 and 2.55 do; a line that only starts
    // like one, in a kept message or commented out in a skipped one, is not a heading.
    [Fact]
    public void WhenALineOnlyStartsLikeANumberedHeadingOfGits_ShouldReadItAsTheMessagesOwn()
    {
        Assert.Equal("Make A\n\nMake C\n\n# The commit message #1 convention is in CONTRIBUTING\nkeep this line\n\nMake D", MergeMessages.Clean(
            "# This is a combination of 3 commits.\n# This is the 1st commit message:\n\nMake A\n\n# This is the commit message #2:\n\nMake C\n\n"
            + "# The commit message #1 convention is in CONTRIBUTING\nkeep this line\n\n# This is the commit message #3:\n\nMake D\n"));
        Assert.Equal("Make A\n\nMake D", MergeMessages.Clean("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\nMake A\n\n"
            + "# The commit message #2 will be skipped:\n\n# fixup! Make A\n#\n# This is the commit message #2: details\n# not part of the fold\n\n"
            + "# This is the commit message #3:\n\nMake D\n"));
        Assert.Equal("Add y\n\n# This is the commit message #5:\nstays", MergeMessages.Clean("# This is a combination of 2 commits.\n"
            + "# This is the 1st commit message:\n\nAdd y\n\n# This is the commit message #5:\nstays\n\n# The commit message #2 will be skipped:\n\n# Make C\n"));
    }

    // A kept message may itself be Git's text for a fold, as earlier Trag builds committed it, even one that holds another:
    // Git leaves their lines out too. The texts are the ones Git 2.43, 2.50 and 2.55 write for a fixup and a squash into
    // such a commit, for one whose text skips a message, for such a commit squashed in as the second message, for a fixup
    // and a squash into a commit whose text holds another one, also when the inner one skips a message, for two such
    // commits squashed in, for a squash into one whose text skips a message, for two squashes into one whose text is for
    // three commits, so its headings #2 and #3 come before the fold's, and for two squashes into one whose text holds
    // another as its second message.
    [Theory]
    [InlineData("# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nAdd y\n\n"
        + "# This is the commit message #2:\n\nMore detail\n\n# The commit message #2 will be skipped:\n\n# Make C\n", "Add y\n\nMore detail")]
    [InlineData("# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nAdd y\n\n"
        + "# This is the commit message #2:\n\nMore detail\n\n# This is the commit message #2:\n\nMake C\n", "Add y\n\nMore detail\n\nMake C")]
    [InlineData("# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nAdd y\n\n"
        + "# The commit message #2 will be skipped:\n\n# Fix y\n\n# The commit message #2 will be skipped:\n\n# Make C\n", "Add y")]
    [InlineData("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\nMake A\n\n# This is the commit message #2:\n\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\n\nAdd y\n\n# This is the commit message #2:\n\nMore detail\n\n# This is the commit message #3:\n\nMake C\n", "Make A\n\nAdd y\n\nMore detail\n\nMake C")]
    [InlineData("# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n"
        + "# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nAdd y\n\n# This is the commit message #2:\n\nMore detail\n\n"
        + "# This is the commit message #2:\n\nMake C\n\n# The commit message #2 will be skipped:\n\n# Make D\n", "Add y\n\nMore detail\n\nMake C")]
    [InlineData("# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n"
        + "# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nAdd y\n\n# This is the commit message #2:\n\nMore detail\n\n"
        + "# This is the commit message #2:\n\nMake C\n\n# This is the commit message #2:\n\nMake D\n", "Add y\n\nMore detail\n\nMake C\n\nMake D")]
    [InlineData("# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n"
        + "# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nAdd y\n\n# This is the commit message #2:\n\nMore detail\n\n"
        + "# The commit message #2 will be skipped:\n\n# Make C\n\n# This is the commit message #2:\n\nMake D\n", "Add y\n\nMore detail\n\nMake D")]
    [InlineData("# This is a combination of 4 commits.\n# This is the 1st commit message:\n\nMake A\n\n# This is the commit message #2:\n\n"
        + "# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nAdd y\n\n# This is the commit message #2:\n\nMore detail\n\n"
        + "# This is the commit message #3:\n\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nAdd z\n\n"
        + "# This is the commit message #2:\n\nZ detail\n\n# This is the commit message #4:\n\nMake D\n", "Make A\n\nAdd y\n\nMore detail\n\nAdd z\n\nZ detail\n\nMake D")]
    [InlineData("# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n"
        + "Add y\n\n# The commit message #2 will be skipped:\n\n# Fix y\n\n# This is the commit message #2:\n\nMake C\n", "Add y\n\nMake C")]
    [InlineData("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\n# This is a combination of 3 commits.\n# This is the 1st commit message:\n\n"
        + "A1\n\n# This is the commit message #2:\n\nA2\n\n# This is the commit message #3:\n\nA3\n\n# This is the commit message #2:\n\nMake S\n\n"
        + "# This is the commit message #3:\n\nMake D\n", "A1\n\nA2\n\nA3\n\nMake S\n\nMake D")]
    [InlineData("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n"
        + "T1\n\n# This is the commit message #2:\n\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nU1\n\n# This is the commit message #2:\n\n"
        + "U2\n\n# This is the commit message #2:\n\nMake S\n\n# This is the commit message #3:\n\nMake D\n", "T1\n\nU1\n\nU2\n\nMake S\n\nMake D")]
    public void WhenAKeptMessageIsItselfGitsTextForAFold_ShouldLeaveGitsLinesOutOfItToo(string message, string expected) =>
        Assert.Equal(expected, MergeMessages.Clean(message));

    // Only Git's exact first line, with a count of two or more, opens a kept message's own text for a fold, and only in a
    // message Git keeps: a kept line that only reads like it stays the message's also inside a kept message's
    // own text for a fold, where it does not hide the fold's later heading, and the lines of a skipped message that Git
    // comments out into that line and the first heading stay out, also when they end the skipped message inside such a
    // text or inside a text that such a text holds, where they do not hide the fold's later heading either, as Git 2.43,
    // 2.50 and 2.55 write them. A later kept message that quotes Git's first line and first heading without an empty line
    // after them does not make the fold's next heading repeat either, so an earlier text that lacks its last heading still
    // ends there: the texts are the ones the same versions write for a squash of such a quote after Trag's commit of Git's
    // text for two commits lacking #2, after one for a fixup -C and a fixup, after one that follows a line of the user's,
    // after one for three commits lacking #3, and after a message that quotes the same lines with an empty line after them,
    // and for a fold of three commits in which such a quote comes between Trag's commit after a line of the user's and
    // Git's complete text for two commits that skips its second message.
    [Theory]
    [InlineData("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\nMake A\n\n# The commit message #2 will be skipped:\n\n# Fold notes\n#\n"
        + "# This is a combination of 2 commits.\n# This is the 1st commit message:\n# secret draft\n\n# This is the commit message #3:\n\nMake C\n", "Make A\n\nMake C")]
    [InlineData("# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nMerge the parsers\n\n# This is a combination of the old and new parser.\n"
        + "# This is the 1st commit message: kept\nsee #12\n\n# The commit message #2 will be skipped:\n\n# Make C\n",
        "Merge the parsers\n\n# This is a combination of the old and new parser.\n# This is the 1st commit message: kept\nsee #12")]
    [InlineData("# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nCount the folds\n\n# This is a combination of commits.\n"
        + "# This is a combination of 1 commits.\n# This is the 1st commit message:\nstays\n\n# The commit message #2 will be skipped:\n\n# Make C\n",
        "Count the folds\n\n# This is a combination of commits.\n# This is a combination of 1 commits.\n# This is the 1st commit message:\nstays")]
    [InlineData("# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nExplain\n\n# This is a combination of 2 changes.\n"
        + "# This is the 1st commit message:\nstays\n\n# The commit message #2 will be skipped:\n\n# Make D\n",
        "Explain\n\n# This is a combination of 2 changes.\n# This is the 1st commit message:\nstays")]
    [InlineData("# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nExplain\n\n# This is a combination of 2 changes.\n"
        + "# This is the 1st commit message:\n\nstays\n\n# The commit message #2 will be skipped:\n\n# Make D\n",
        "Explain\n\n# This is a combination of 2 changes.\n# This is the 1st commit message:\n\nstays")]
    [InlineData("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\nExplain squash\n\nGit starts the text with:\n"
        + "# This is a combination of 2 commits.\n# This is the 1st commit message:\nand keeps the first message.\n\n# This is the commit message #2:\n\nMake S\n\n"
        + "# This is the commit message #3:\n\nMake D\n",
        "Explain squash\n\nGit starts the text with:\n# This is a combination of 2 commits.\n# This is the 1st commit message:\nand keeps the first message.\n\nMake S\n\nMake D")]
    [InlineData("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\nExplain squash\n\nGit starts the text with:\n"
        + "# This is a combination of 2 commits.\n# This is the 1st commit message:\nand keeps the first message.\n\n# This is the commit message #2:\n\nMake S\n\n"
        + "# The commit message #3 will be skipped:\n\n# Make D\n",
        "Explain squash\n\nGit starts the text with:\n# This is a combination of 2 commits.\n# This is the 1st commit message:\nand keeps the first message.\n\nMake S")]
    [InlineData("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n"
        + "Add y\n\n# This is the commit message #2:\n\nExplain squash\n\nGit starts the text with:\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\nand keeps the first message.\n\n# This is the commit message #2:\n\nMake S\n\n# This is the commit message #3:\n\nMake C\n",
        "Add y\n\nExplain squash\n\nGit starts the text with:\n# This is a combination of 2 commits.\n# This is the 1st commit message:\nand keeps the first message.\n\n"
        + "Make S\n\nMake C")]
    [InlineData("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n"
        + "Add y\n\n# The commit message #2 will be skipped:\n\n# Fold notes\n#\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n"
        + "# This is the commit message #2:\n\nMake S\n\n# This is the commit message #3:\n\nMake C\n", "Add y\n\nMake S\n\nMake C")]
    [InlineData("# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\n\nM1\n\n# This is the commit message #2:\n\n# This is a combination of 2 commits.\n"
        + "# The 1st commit message will be skipped:\n\n# Fold notes\n#\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n"
        + "# This is the commit message #2:\n\nM2\n\n# This is the commit message #2:\n\nM3\n", "M1\n\nM2\n\nM3")]
    [InlineData("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\n\nM1\n\n# This is the commit message #2:\n\nE2\n\nGit starts the text with:\n"
        + "# This is a combination of 2 commits.\n# This is the 1st commit message:\nK3\n\n# This is the commit message #3:\n\nM4\n",
        "M1\n\nE2\n\nGit starts the text with:\n# This is a combination of 2 commits.\n# This is the 1st commit message:\nK3\n\nM4")]
    [InlineData("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n"
        + "# The 1st commit message will be skipped:\n\n# M1\n\n# The commit message #2 will be skipped:\n\n# M2\n\n# This is the commit message #3:\n\nE3\n\n"
        + "Git starts the text with:\n# This is a combination of 2 commits.\n# This is the 1st commit message:\nK4\n",
        "E3\n\nGit starts the text with:\n# This is a combination of 2 commits.\n# This is the 1st commit message:\nK4")]
    [InlineData("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\nP1\n\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\n\nM2\n\n# This is the commit message #2:\n\nM3\n\n# This is the commit message #3:\n\nE4\n\n"
        + "Git starts the text with:\n# This is a combination of 2 commits.\n# This is the 1st commit message:\nK5\n",
        "P1\n\nM2\n\nM3\n\nE4\n\nGit starts the text with:\n# This is a combination of 2 commits.\n# This is the 1st commit message:\nK5")]
    [InlineData("# This is a combination of 4 commits.\n# This is the 1st commit message:\n\n# This is a combination of 3 commits.\n"
        + "# This is the 1st commit message:\n\nM1\n\n# This is the commit message #2:\n\nM2\n\n# This is the commit message #2:\n\nM3\n\n"
        + "# This is the commit message #3:\n\nM4\n\n# This is the commit message #4:\n\nE5\n\nGit starts the text with:\n"
        + "# This is a combination of 2 commits.\n# This is the 1st commit message:\nK6\n",
        "M1\n\nM2\n\nM3\n\nM4\n\nE5\n\nGit starts the text with:\n# This is a combination of 2 commits.\n# This is the 1st commit message:\nK6")]
    [InlineData("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\nQ1\n\nGit starts the text with:\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\n\nand keeps the first message.\n\n# This is the commit message #2:\n\nM3\n\n# This is the commit message #3:\n\n"
        + "E4\n\nGit starts the text with:\n# This is a combination of 2 commits.\n# This is the 1st commit message:\nK5\n",
        "Q1\n\nGit starts the text with:\n\nand keeps the first message.\n\nM3\n\nE4\n\nGit starts the text with:\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\nK5")]
    [InlineData("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\nP2\n\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\n\nM1\n\n# This is the commit message #2:\n\nE3\n\nGit starts the text with:\n"
        + "# This is a combination of 2 commits.\n# This is the 1st commit message:\nK4\n\n# This is the commit message #3:\n\n"
        + "# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nM5\n\n# The commit message #2 will be skipped:\n\n# M6\n",
        "P2\n\nM1\n\nE3\n\nGit starts the text with:\n# This is a combination of 2 commits.\n# This is the 1st commit message:\nK4\n\nM5")]
    public void WhenALineIsNotGitsFirstLineOfAFoldInAKeptMessage_ShouldReadNoFoldFromIt(string message, string expected) =>
        Assert.Equal(expected, MergeMessages.Clean(message));

    // A kept message's own text for a fold may lack its last headings: a message that quotes Git's first line and first
    // heading with an empty line after them, or ends with them and a line break, an earlier Trag build's commit of Git's text
    // with the skipped message deleted, which is byte for byte the text Git itself cuts at the scissors line under
    // commit.cleanup=scissors, and a text for three commits with two headings. The fold's next heading ends it, so the
    // fold's later headings stay out and the user's squash message stays in, and a skipped message's line that Git
    // comments into the heading the text still lacks stays out too. A later such text whose missing heading has the number
    // of a later heading of the fold ends there as well, so the messages between the two texts stay in. The texts are the
    // ones Git 2.43, 2.50 and 2.55 write for folds of three and four commits, for folds of five commits with two and with
    // three such texts, two of them skipping their first message, for a fold of four commits whose first message is a
    // complete such text, as Git commits it under commit.cleanup=scissors, that ends with one lacking its last heading, for
    // folds of five commits with Trag's commit or a quote with an empty line after it, a fixup, a text for four commits
    // lacking #4, a fixup and a text for two or three commits, for a fold of four commits with three such texts and a
    // fixup, and, from Git 2.43, 2.45, 2.50 and 2.55 alike, for folds of five and six commits with Trag's commit of a text
    // for two lacking #2, a fixup or a squash, a text for four or five commits lacking its last headings, one or two
    // fixups and a complete text for four or five commits, whose own headings carry the numbers the earlier text still
    // waits for.
    [Theory]
    [InlineData("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\nExplain squash\n\nGit starts the text with:\n"
        + "# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nand keeps the first message.\n\n# This is the commit message #2:\n\nMake S\n\n"
        + "# This is the commit message #3:\n\nMake D\n", "Explain squash\n\nGit starts the text with:\n\nand keeps the first message.\n\nMake S\n\nMake D")]
    [InlineData("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\nExplain squash\n\nGit starts the text with:\n\n"
        + "# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n# The commit message #2 will be skipped:\n\n# Make S\n\n"
        + "# This is the commit message #3:\n\nMake D\n", "Explain squash\n\nGit starts the text with:\n\nMake D")]
    [InlineData("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n"
        + "Add y\n\n# This is the commit message #2:\n\nMake S\n\n# This is the commit message #3:\n\nMake D\n", "Add y\n\nMake S\n\nMake D")]
    [InlineData("# This is a combination of 4 commits.\n# This is the 1st commit message:\n\nMake A\n\n# This is the commit message #2:\n\n"
        + "# This is a combination of 3 commits.\n# This is the 1st commit message:\n\nAdd y\n\n# This is the commit message #2:\n\nMore detail\n\n"
        + "# This is the commit message #3:\n\nMake S\n\n# This is the commit message #4:\n\nMake D\n", "Make A\n\nAdd y\n\nMore detail\n\nMake S\n\nMake D")]
    [InlineData("# This is a combination of 4 commits.\n# This is the 1st commit message:\n\nMake A\n\n# This is the commit message #2:\n\nExplain squash\n\n"
        + "Git starts the text with:\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nand keeps the first message.\n\n"
        + "# The commit message #3 will be skipped:\n\n# Fold notes\n#\n# This is the commit message #2:\n# secret draft\n\n# This is the commit message #4:\n\nMake D\n",
        "Make A\n\nExplain squash\n\nGit starts the text with:\n\nand keeps the first message.\n\nMake D")]
    [InlineData("# This is a combination of 4 commits.\n# This is the 1st commit message:\n\n# This is a combination of 3 commits.\n"
        + "# This is the 1st commit message:\n\nM1\n\n# This is the commit message #2:\n\nM2\n\n# This is the commit message #3:\n\n"
        + "# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nM3\n\n# This is the commit message #2:\n\nM4\n\nD5\n\n"
        + "# This is the commit message #3:\n\nM6\n\n# The commit message #4 will be skipped:\n\n# M7\n", "M1\n\nM2\n\nM3\n\nM4\n\nD5\n\nM6")]
    [InlineData("# This is a combination of 5 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\n\nM1\n\n# The commit message #2 will be skipped:\n\n# M2\n\n# This is the commit message #3:\n\n"
        + "# This is a combination of 4 commits.\n# This is the 1st commit message:\n\nM3\n\n# This is the commit message #2:\n\nM4\n\n"
        + "# This is the commit message #3:\n\nM5\n\n# This is the commit message #4:\n\nM6\n\n# This is the commit message #5:\n\nM7\n", "M1\n\nM3\n\nM4\n\nM5\n\nM6\n\nM7")]
    [InlineData("# This is a combination of 5 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\n\nM1\n\nD2\n\n# This is the commit message #2:\n\n# This is a combination of 4 commits.\n"
        + "# The 1st commit message will be skipped:\n\n# M3\n#\n# D4\n\n# This is the commit message #2:\n\nM5\n\n# This is the commit message #3:\n\nM6\n\n"
        + "# The commit message #3 will be skipped:\n\n# M7\n\n# This is the commit message #4:\n\n# This is a combination of 2 commits.\n"
        + "# The 1st commit message will be skipped:\n\n# M8\n#\n# D9\n\n# This is the commit message #5:\n\nM10\n\nD11\n", "M1\n\nD2\n\nM5\n\nM6\n\nM10\n\nD11")]
    [InlineData("# This is a combination of 5 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\n\nM1\n\n# The commit message #2 will be skipped:\n\n# M2\n\n# This is the commit message #3:\n\n"
        + "# This is a combination of 4 commits.\n# This is the 1st commit message:\n\nM3\n\n# This is the commit message #2:\n\nM4\n\n"
        + "# This is the commit message #3:\n\nM5\n\n# The commit message #4 will be skipped:\n\n# M6\n\n# This is the commit message #5:\n\n"
        + "# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nK1\n\n# This is the commit message #2:\n\nK2\n",
        "M1\n\nM3\n\nM4\n\nM5\n\nK1\n\nK2")]
    [InlineData("# This is a combination of 5 commits.\n# This is the 1st commit message:\n\nE1\n\nGit starts the text with:\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\n\nK2\n\n# The commit message #2 will be skipped:\n\n# M2\n\n# This is the commit message #3:\n\nP3\n\n"
        + "# This is a combination of 4 commits.\n# This is the 1st commit message:\n\nM3\n\n# This is the commit message #2:\n\nM4\n\n"
        + "# The commit message #3 will be skipped:\n\n# M5\n\n# The commit message #4 will be skipped:\n\n# M6\n\n# This is the commit message #5:\n\n"
        + "# This is a combination of 3 commits.\n# This is the 1st commit message:\n\nK1\n\n# This is the commit message #2:\n\nK2\n",
        "E1\n\nGit starts the text with:\n\nK2\n\nP3\n\nM3\n\nM4\n\nK1\n\nK2")]
    [InlineData("# This is a combination of 4 commits.\n# This is the 1st commit message:\n\n# This is a combination of 3 commits.\n"
        + "# This is the 1st commit message:\n\nM1\n\n# This is the commit message #2:\n\n# This is a combination of 4 commits.\n"
        + "# This is the 1st commit message:\n\nM2\n\n# This is the commit message #2:\n\nM3\n\n# This is the commit message #3:\n\nM4\n\n"
        + "# The commit message #3 will be skipped:\n\n# M5\n#\n# D6\n\n# This is the commit message #4:\n\n# This is a combination of 3 commits.\n"
        + "# This is the 1st commit message:\n\nM7\n\n# This is the commit message #2:\n\nM8\n\nD9\n", "M1\n\nM2\n\nM3\n\nM4\n\nM7\n\nM8\n\nD9")]
    [InlineData("# This is a combination of 5 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\n\nM1\n\n# The commit message #2 will be skipped:\n\n# M2\n\n# This is the commit message #3:\n\n"
        + "# This is a combination of 4 commits.\n# This is the 1st commit message:\n\nM3\n\n# This is the commit message #2:\n\nM4\n\n"
        + "# This is the commit message #3:\n\nM5\n\n# The commit message #4 will be skipped:\n\n# M6\n\n# This is the commit message #5:\n\n"
        + "# This is a combination of 4 commits.\n# This is the 1st commit message:\n\nK1\n\n# This is the commit message #2:\n\nK2\n\n"
        + "# This is the commit message #3:\n\nK3\n\n# This is the commit message #4:\n\nK4\n", "M1\n\nM3\n\nM4\n\nM5\n\nK1\n\nK2\n\nK3\n\nK4")]
    [InlineData("# This is a combination of 5 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\n\nM1\n\n# This is the commit message #2:\n\nM2\n\n# This is the commit message #3:\n\n"
        + "# This is a combination of 4 commits.\n# This is the 1st commit message:\n\nM3\n\n# This is the commit message #2:\n\nM4\n\n"
        + "# This is the commit message #3:\n\nM5\n\n# The commit message #4 will be skipped:\n\n# M6\n\n# This is the commit message #5:\n\n"
        + "# This is a combination of 4 commits.\n# This is the 1st commit message:\n\nK1\n\n# This is the commit message #2:\n\nK2\n\n"
        + "# The commit message #3 will be skipped:\n\n# K3\n\n# This is the commit message #4:\n\nK4\n", "M1\n\nM2\n\nM3\n\nM4\n\nM5\n\nK1\n\nK2\n\nK4")]
    [InlineData("# This is a combination of 5 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\n\nM1\n\nD2\n\n# This is the commit message #2:\n\nM3\n\n# This is the commit message #3:\n\n"
        + "# This is a combination of 4 commits.\n# This is the 1st commit message:\n\nM4\n\n# The commit message #2 will be skipped:\n\n# M5\n#\n# D6\n\n"
        + "# This is the commit message #3:\n\nM7\n\n# The commit message #4 will be skipped:\n\n# M8\n#\n# D9\n\n# This is the commit message #5:\n\n"
        + "# This is a combination of 4 commits.\n# This is the 1st commit message:\n\nM10\n\n# The commit message #2 will be skipped:\n\n# M11\n\n"
        + "# This is the commit message #3:\n\nM12\n\n# This is the commit message #4:\n\nM13\n", "M1\n\nD2\n\nM3\n\nM4\n\nM7\n\nM10\n\nM12\n\nM13")]
    [InlineData("# This is a combination of 6 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\n\nM1\n\n# The commit message #2 will be skipped:\n\n# M2\n\n# This is the commit message #3:\n\n"
        + "# This is a combination of 5 commits.\n# This is the 1st commit message:\n\nM3\n\n# This is the commit message #2:\n\nM4\n\n"
        + "# This is the commit message #3:\n\nM5\n\n# The commit message #4 will be skipped:\n\n# M6\n\n# The commit message #5 will be skipped:\n\n# M7\n\n"
        + "# This is the commit message #6:\n\n# This is a combination of 5 commits.\n# This is the 1st commit message:\n\nK1\n\n"
        + "# This is the commit message #2:\n\nK2\n\n# This is the commit message #3:\n\nK3\n\n# This is the commit message #4:\n\nK4\n\n"
        + "# This is the commit message #5:\n\nK5\n", "M1\n\nM3\n\nM4\n\nM5\n\nK1\n\nK2\n\nK3\n\nK4\n\nK5")]
    public void WhenAKeptMessagesTextForAFoldLacksItsLastHeadings_ShouldEndItAtTheFoldsNextHeading(string message, string expected) =>
        Assert.Equal(expected, MergeMessages.Clean(message));

    // A kept message's own text for a fold may hold another one, as Git commits a squash under commit.cleanup=scissors or
    // earlier Trag builds committed Git's text, and either may lack its last headings. A heading that would be the inner
    // text's last goes to the text around it when that text waits for the same number and further down the heading of the
    // number after it comes first: the inner text lacks its last headings. It stays the inner text's when the inner text
    // has more headings to come, when the text around it waits for another number, and when this number comes again first.
    // The texts are the ones Git 2.43, 2.50 and 2.55 write, byte for byte the same, for a squash into a complete such text
    // for three commits whose first message is one for two lacking its last heading, into one for two lacking its last
    // heading whose first message is a complete one for three, and into a complete one for three whose second message is
    // a complete one for two that skips its second, and for one for three lacking its last heading whose first message is
    // a complete one for two, squashed in as the second message of a fold of three.
    [Theory]
    [InlineData("# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n# This is a combination of 3 commits.\n"
        + "# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nM1\n\nD2\n\n"
        + "# The commit message #2 will be skipped:\n\n# M3\n#\n# D4\n\n# This is the commit message #3:\n\nM5\n\n# This is the commit message #2:\n\nM6\n",
        "M1\n\nD2\n\nM5\n\nM6")]
    [InlineData("# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\n\n# This is a combination of 3 commits.\n# This is the 1st commit message:\n\nM1\n\n"
        + "# This is the commit message #2:\n\nM2\n\n# This is the commit message #3:\n\nM3\n\n# This is the commit message #2:\n\nM4\n", "M1\n\nM2\n\nM3\n\nM4")]
    [InlineData("# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n# This is a combination of 3 commits.\n"
        + "# This is the 1st commit message:\n\nM1\n\n# This is the commit message #2:\n\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\n\nM2\n\n# The commit message #2 will be skipped:\n\n# M3\n\n# This is the commit message #3:\n\nM4\n\n"
        + "# This is the commit message #2:\n\nM5\n\nD6\n", "M1\n\nM2\n\nM4\n\nM5\n\nD6")]
    [InlineData("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\nM1\n\n# This is the commit message #2:\n\n"
        + "# This is a combination of 3 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\n\nM2\n\n# This is the commit message #2:\n\nM3\n\n# This is the commit message #2:\n\nM4\n\n"
        + "# This is the commit message #3:\n\nM5\n", "M1\n\nM2\n\nM3\n\nM4\n\nM5")]
    public void WhenATextForAFoldInAKeptMessageHoldsAnother_ShouldGiveEachHeadingToTheTextItBelongsTo(string message, string expected) =>
        Assert.Equal(expected, MergeMessages.Clean(message));

    // A kept message's own text for a fold whose kept message quotes the fold's next heading still ends at its own last
    // heading, the one over the message it skips, as Git 2.43, 2.50 and 2.55 write it: the fold's heading of that number
    // comes again further down.
    [Fact]
    public void WhenAKeptMessagesTextForAFoldQuotesTheFoldsNextHeading_ShouldEndThatTextAtItsOwnLastHeading() =>
        Assert.Equal("Make A\n\nAdd y\n\nGit heading quoted:\n\nMake D", MergeMessages.Clean("# This is a combination of 4 commits.\n"
            + "# This is the 1st commit message:\n\nMake A\n\n# This is the commit message #2:\n\n# This is a combination of 2 commits.\n"
            + "# This is the 1st commit message:\n\nAdd y\n\nGit heading quoted:\n# This is the commit message #3:\n\n# The commit message #2 will be skipped:\n\n"
            + "# Make C\n\n# The commit message #3 will be skipped:\n\n# Fold notes\n\n# This is the commit message #4:\n\nMake D\n"));

    // A later kept message may be Git's complete text for a fold, as earlier Trag builds committed it or as Git commits a
    // squash under commit.cleanup=scissors, with headings of its own numbered from 1. They are not the fold's, so an
    // earlier text that lacks its last headings still ends at the fold's next heading, and a skipped message's line that
    // Git comments into the heading that text still lacks stays out. The texts are the ones Git 2.43, 2.50 and 2.55 write,
    // byte for byte the same, for a squash of such a text for two and for three commits after Trag's commit of Git's text
    // with the skipped message deleted, after a message that quotes Git's first line and first heading with an empty line
    // after them, after that quote and a skipped message whose line Git comments into the heading it lacks, after Trag's
    // commit, a squash and a fixup, for a squash of such a text for three commits right after Trag's commit, for one whose
    // first message is itself such a text, for one after a text for three commits with two headings, a squash and the
    // quote, both texts lacking their last headings, for a squash of such a text for two commits that skips its first
    // message after Trag's commit and a squash, and for a fold of four commits with Trag's commit, such a text for four
    // commits that skips its third message, Trag's commit again and a squash.
    [Theory]
    [InlineData("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n"
        + "Add y\n\n# This is the commit message #2:\n\nMake S\n\n# This is the commit message #3:\n\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\n\nC1\n\n# This is the commit message #2:\n\nC2\n", "Add y\n\nMake S\n\nC1\n\nC2")]
    [InlineData("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n"
        + "Add y\n\n# This is the commit message #2:\n\nMake S\n\n# This is the commit message #3:\n\n# This is a combination of 3 commits.\n"
        + "# This is the 1st commit message:\n\nC1\n\n# This is the commit message #2:\n\nC2\n\n# This is the commit message #3:\n\nC3\n", "Add y\n\nMake S\n\nC1\n\nC2\n\nC3")]
    [InlineData("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\nExplain squash\n\nGit starts the text with:\n"
        + "# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nand keeps the first message.\n\n# This is the commit message #2:\n\nMake S\n\n"
        + "# This is the commit message #3:\n\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nC1\n\n# This is the commit message #2:\n\nC2\n",
        "Explain squash\n\nGit starts the text with:\n\nand keeps the first message.\n\nMake S\n\nC1\n\nC2")]
    [InlineData("# This is a combination of 4 commits.\n# This is the 1st commit message:\n\nMake A\n\n# This is the commit message #2:\n\nExplain squash\n\n"
        + "Git starts the text with:\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nand keeps the first message.\n\n"
        + "# The commit message #3 will be skipped:\n\n# Fold notes\n#\n# This is the commit message #2:\n# secret draft\n\n# This is the commit message #4:\n\n"
        + "# This is a combination of 3 commits.\n# This is the 1st commit message:\n\nC1\n\n# This is the commit message #2:\n\nC2\n\n# This is the commit message #3:\n\nC3\n",
        "Make A\n\nExplain squash\n\nGit starts the text with:\n\nand keeps the first message.\n\nC1\n\nC2\n\nC3")]
    [InlineData("# This is a combination of 4 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n"
        + "Add y\n\n# This is the commit message #2:\n\nMake S\n\n# The commit message #3 will be skipped:\n\n# Fix S\n\n# This is the commit message #4:\n\n"
        + "# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nC1\n\n# This is the commit message #2:\n\nC2\n", "Add y\n\nMake S\n\nC1\n\nC2")]
    [InlineData("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n"
        + "Add y\n\n# This is the commit message #2:\n\n# This is a combination of 3 commits.\n# This is the 1st commit message:\n\nC1\n\n# This is the commit message #2:\n\n"
        + "C2\n\n# This is the commit message #3:\n\nC3\n\n# This is the commit message #3:\n\nMake D\n", "Add y\n\nC1\n\nC2\n\nC3\n\nMake D")]
    [InlineData("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\n"
        + "Add y\n\n# This is the commit message #2:\n\nMake S\n\n# This is the commit message #3:\n\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nN1\n\n# This is the commit message #2:\n\n"
        + "N2\n\n# This is the commit message #2:\n\nC2\n", "Add y\n\nMake S\n\nN1\n\nN2\n\nC2")]
    [InlineData("# This is a combination of 5 commits.\n# This is the 1st commit message:\n\nMake A\n\n# This is the commit message #2:\n\n"
        + "# This is a combination of 3 commits.\n# This is the 1st commit message:\n\nAdd y\n\n# This is the commit message #2:\n\nMore detail\n\n"
        + "# This is the commit message #3:\n\nMake S\n\n# This is the commit message #4:\n\nExplain squash\n\nGit starts the text with:\n"
        + "# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nand keeps the first message.\n\n# This is the commit message #5:\n\n"
        + "# This is a combination of 3 commits.\n# This is the 1st commit message:\n\nC1\n\n# This is the commit message #2:\n\nC2\n\n# This is the commit message #3:\n\nC3\n",
        "Make A\n\nAdd y\n\nMore detail\n\nMake S\n\nExplain squash\n\nGit starts the text with:\n\nand keeps the first message.\n\nC1\n\nC2\n\nC3")]
    [InlineData("# This is a combination of 3 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\n\nM1\n\n# This is the commit message #2:\n\nM2\n\nD3\n\n# This is the commit message #3:\n\n"
        + "# This is a combination of 2 commits.\n# The 1st commit message will be skipped:\n\n# M4\n\n# This is the commit message #2:\n\nM5\n",
        "M1\n\nM2\n\nD3\n\nM5")]
    [InlineData("# This is a combination of 4 commits.\n# This is the 1st commit message:\n\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\n\nM1\n\n# This is the commit message #2:\n\n# This is a combination of 4 commits.\n"
        + "# This is the 1st commit message:\n\nM2\n\n# This is the commit message #2:\n\nM3\n\n# The commit message #3 will be skipped:\n\n# M4\n\n"
        + "# This is the commit message #4:\n\nM5\n\n# This is the commit message #3:\n\n# This is a combination of 2 commits.\n"
        + "# This is the 1st commit message:\n\nM6\n\n# This is the commit message #4:\n\nM7\n", "M1\n\nM2\n\nM3\n\nM5\n\nM6\n\nM7")]
    public void WhenALaterKeptMessageIsGitsCompleteTextForAFold_ShouldStillEndAnEarlierTextLackingItsLastHeadingsAtTheFoldsNextHeading(string message, string expected) =>
        Assert.Equal(expected, MergeMessages.Clean(message));

    // Git comments out a subject that starts with "amend!" with or without a space after it, and the paragraph ends at a
    // line of only spaces, as Git 2.43 and 2.55 write them.
    [Fact]
    public void WhenGitCommentedOutAMarkedSubjectParagraph_ShouldEndItWhereGitDoes()
    {
        Assert.Equal("New A", MergeMessages.Clean("# This is a combination of 2 commits.\n# The 1st commit message will be skipped:\n\n# C\n\n"
            + "# This is the commit message #2:\n\n# amend!Make A\n\nNew A\n"));
        Assert.Equal("C\n\nD body", MergeMessages.Clean("# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nC\n\n"
            + "# This is the commit message #2:\n\n# squash! Make A\n   \nD body\n"));
    }

    // A cherry-pick of a commit whose message is Git's text for a fold, as earlier Trag builds committed it, stops with Git's
    // list of conflicts after that text.
    [Fact]
    public void WhenGitsTextForAFoldEndsWithAConflictList_ShouldLeaveTheListOutToo() =>
        Assert.Equal("Add y\n\nMore detail", MergeMessages.Clean("# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nAdd y\n\n"
            + "# This is the commit message #2:\n\nMore detail\n\n# Conflicts:\n#\tf\n"));

    // Only Git's own two headings mark a message it skips; a kept line that merely reads like one is the message's.
    [Fact]
    public void WhenAKeptMessageHasALineThatOnlyReadsLikeAHeadingOfGits_ShouldKeepTheLinesAfterIt() =>
        Assert.Equal("Subject\n\n# The flaky tests will be skipped:\n- one\n- two", MergeMessages.Clean("# This is a combination of 2 commits.\n"
            + "# This is the 1st commit message:\n\nSubject\n\n# The flaky tests will be skipped:\n- one\n- two\n\n# The commit message #2 will be skipped:\n\n# Make C\n"));

    [Theory]
    [InlineData("Fix: This is a combination of two fixes\n\nFix: both")]
    [InlineData("WIP: This is a combination of 2 commits.")]
    [InlineData("Fix: This is a combination of 3 commits.\n\nFix: typo in the header\nAlso the docs")]
    public void WhenTheMessagesFirstLineOnlyReadsLikeGitsTextForAFold_ShouldKeepItWhole(string message) =>
        Assert.Equal(message, MergeMessages.Clean(message + "\n"));

    // Every message of this fold is Trag's commit of Git's text for one commit more, lacking its last heading, so each later
    // heading of the fold is also the next heading of the text open above it and the check whether it comes again asks
    // itself there; without keeping its answers a fold of 25 commits takes hours.
    [Fact]
    public async Task WhenEveryMessageOfALongFoldIsATextLackingItsLastHeading_ShouldCleanItAtOnce()
    {
        var clean = Task.Run(() => MergeMessages.Clean(FoldOfTextsLackingTheirLastHeading(25)));
        Assert.Same(clean, await Task.WhenAny(clean, Task.Delay(TimeSpan.FromSeconds(10))));
    }

    private static string FoldOfTextsLackingTheirLastHeading(int count)
    {
        static string Heading(int number) => number == 1 ? "# This is the 1st commit message:" : "# This is the commit message #" + number + ":";
        var lines = new List<string> { "# This is a combination of " + count + " commits." };
        for (var message = 1; message <= count; message++)
        {
            if (message > 1) lines.Add("");
            lines.AddRange([Heading(message), ""]);
            if (message == count)
            {
                lines.Add("M" + message);
                continue;
            }
            lines.Add("# This is a combination of " + (message + 1) + " commits.");
            for (var inner = 1; inner <= message; inner++)
            {
                if (inner > 1) lines.Add("");
                lines.AddRange([Heading(inner), "", "M" + message + "." + inner]);
            }
        }
        return string.Join("\n", lines) + "\n";
    }
}
