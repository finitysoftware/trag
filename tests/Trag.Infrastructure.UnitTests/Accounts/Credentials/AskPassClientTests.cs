using Trag.Infrastructure.Accounts.Credentials;

namespace Trag.Infrastructure.UnitTests.Accounts.Credentials;

// Git starts Trag as its askpass helper with the marker, the pipe and the operation Trag set; anything else is refused
// before a pipe is opened.
public sealed class AskPassClientTests
{
    private static Func<string, string?> Environment(params (string Name, string Value)[] variables) =>
        name => variables.FirstOrDefault(v => v.Name == name).Value;

    [Fact]
    public void WhenTheMarkerIsSet_ShouldBeAnAskPassLaunch()
    {
        Assert.True(AskPassClient.IsAskPassLaunch(Environment(("TRAG_ASKPASS", "1"))));
        Assert.False(AskPassClient.IsAskPassLaunch(Environment()));
    }

    [Theory]
    [InlineData(null, "trag-test", false)]
    [InlineData("1", "../unsafe", false)]
    [InlineData("1", "trag-test", true)]
    public void WhenAskPassIsStarted_ShouldNeedTheMarkerAndASafePipeNameBeforeAnyUi(string? marker, string? pipe, bool expected) =>
        Assert.Equal(expected, AskPassClient.IsValidContext(marker, pipe, ["prompt"]));

    // A context Trag did not set up answers 2 without touching a pipe.
    [Fact]
    public void WhenTheOperationIsNotOneTragNamed_ShouldRefuseWithoutAnswering()
    {
        using var output = new MemoryStream();
        var environment = Environment(("TRAG_ASKPASS", "1"), ("TRAG_ASKPASS_PIPE", "trag-test"), ("TRAG_ASKPASS_OPERATION", "not-a-guid"));
        Assert.Equal(2, AskPassClient.RunFromEnvironment(environment, ["Password: "], output));
        Assert.Equal(0, output.Length);
    }
}
