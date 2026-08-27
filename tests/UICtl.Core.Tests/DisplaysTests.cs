using UICtl.Core;

namespace UICtl.Core.Tests;

public class DisplaysTests
{
    [Fact]
    public void List_ReturnsAtLeastOneDisplayWithExactlyOnePrimary()
    {
        var displays = Displays.List();

        Assert.NotEmpty(displays);
        Assert.Single(displays, d => d.IsMain);
    }

    [Fact]
    public void List_IndexesMatchPositionAndScaleIsPositive()
    {
        var displays = Displays.List();

        for (int i = 0; i < displays.Count; i++)
        {
            Assert.Equal(i, displays[i].Index);
            Assert.True(displays[i].Scale > 0);
            Assert.True(displays[i].Frame.W > 0 && displays[i].Frame.H > 0);
        }
    }
}
