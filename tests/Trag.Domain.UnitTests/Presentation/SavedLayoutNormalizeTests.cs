using Trag.Domain.Presentation;

namespace Trag.Domain.UnitTests.Presentation;

public sealed class SavedLayoutNormalizeTests
{
    [Fact]
    public void WhenARatioIsNotAFiniteNumber_ShouldFallBackToItsDefault() =>
        Assert.Equal(new SavedLayout(0, 0.5, 0.36, 0, 0.45, 0.5, 1),
            new SavedLayout(0, double.NaN, double.PositiveInfinity, 0, double.NegativeInfinity, double.NaN, double.NaN).Normalize());

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(5, 5)]
    [InlineData(6, 0)]
    public void WhenAThemeIsNormalized_ShouldKeepTheSixThemesAndMakeAnyOtherSystem(int theme, int expected) => Assert.Equal(expected, new SavedLayout(Theme: theme).Normalize().Theme);

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 0)]
    public void WhenAModeIsNormalized_ShouldBeSplitOnlyForModeOne(int mode, int expected) => Assert.Equal(expected, new SavedLayout(mode).Normalize().Mode);

    [Theory]
    [InlineData(0.2, 0.5)]
    [InlineData(0.75, 0.75)]
    [InlineData(1.5, 1)]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    public void WhenACommitRatioIsNormalized_ShouldStayInTheUpperHalfAndTakeZeroOrLessAsNaturalHeight(double ratio, double expected) =>
        Assert.Equal(expected, new SavedLayout(CommitRatio: ratio).Normalize().CommitRatio);

    [Theory]
    [InlineData(1.5, 1)]
    [InlineData(-0.5, 0)]
    [InlineData(0.25, 0.25)]
    public void WhenThePaneRatiosAreNormalized_ShouldBeClampedBetweenZeroAndOne(double ratio, double expected)
    {
        var normalized = new SavedLayout(SplitRatio: ratio, FilesRatio: ratio, HistoryRatio: ratio, StageRatio: ratio).Normalize();
        Assert.Equal(expected, normalized.SplitRatio); Assert.Equal(expected, normalized.FilesRatio);
        Assert.Equal(expected, normalized.HistoryRatio); Assert.Equal(expected, normalized.StageRatio);
    }

    // The message keeps a height of its own, 0 when it is collapsed; a negative one is none chosen yet, and so is anything
    // that is no height.
    [Theory]
    [InlineData(150, 150)]
    [InlineData(0, 0)]
    [InlineData(-20, -1)]
    [InlineData(double.NaN, -1)]
    [InlineData(double.PositiveInfinity, -1)]
    public void WhenAMessageHeightIsNormalized_ShouldKeepAHeightAndTakeAnythingElseAsNoneChosen(double height, double expected) =>
        Assert.Equal(expected, new SavedLayout(MessageHeight: height).Normalize().MessageHeight);

    [Fact]
    public void WhenASavedChoiceIsRead_ShouldKeepOneOfTheFourAndTakeAnyOtherAsEveryMinute()
    {
        Assert.Equal([0, 1, 5, 15, 1, 1], new[] { 0, 1, 5, 15, 7, -1 }.Select(minutes => new SavedLayout(AutoFetch: minutes).Normalize().AutoFetch));
    }
}
