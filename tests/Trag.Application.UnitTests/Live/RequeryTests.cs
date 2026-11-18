using CommunityToolkit.Mvvm.Input;
using Trag.Application.Live;

namespace Trag.Application.UnitTests.Live;

public sealed class RequeryTests
{
    [Fact]
    public void WhenInvalidated_ShouldReachEveryTrackedCommandAndThenTheDerivedProperties()
    {
        var requery = new Requery();
        var order = new List<string>();
        var first = new RelayCommand(() => { }); first.CanExecuteChanged += (_, _) => order.Add("first");
        var second = new AsyncRelayCommand(() => Task.CompletedTask); second.CanExecuteChanged += (_, _) => order.Add("second");
        requery.Track(first);
        requery.Track(second);
        requery.Suggested += () => order.Add("derived");

        requery.Invalidate();

        Assert.Equal(["first", "second", "derived"], order);
    }

    [Fact]
    public void WhenInvalidatedDuringAPass_ShouldRunOnceMoreAfterIt()
    {
        var requery = new Requery();
        var passes = 0;
        var command = new RelayCommand(() => { });
        command.CanExecuteChanged += (_, _) => passes++;
        requery.Track(command);
        var asked = false;
        requery.Suggested += () =>
        {
            if (asked) return;
            asked = true;
            requery.Invalidate(); // a derived property changed state that the commands have to see
            requery.Invalidate(); // asking twice during one pass still means one more pass
        };

        requery.Invalidate();

        Assert.Equal(2, passes);
    }
}
