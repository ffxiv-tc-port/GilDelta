using GilDelta.Storage;

namespace GilDelta.Tests.Storage;

public class SaveDebounceTests
{
    [Fact]
    public void FirstSampleIsTheBaselineAndIsNeverSaved()
    {
        var d = new SaveDebounce();
        Assert.False(d.ShouldSave("a"));
        Assert.False(d.ShouldSave("a"));
        Assert.False(d.ShouldSave("a"));
    }

    [Fact]
    public void ChangeIsSavedOnceTwoSamplesAgree()
    {
        var d = new SaveDebounce();
        d.ShouldSave("a");
        Assert.False(d.ShouldSave("b"));
        Assert.True(d.ShouldSave("b"));
        Assert.False(d.ShouldSave("b"));
    }

    [Fact]
    public void ValueStillMovingIsNotSavedUntilItSettles()
    {
        var d = new SaveDebounce();
        d.ShouldSave("x0");
        for (var i = 1; i <= 10; i++)
            Assert.False(d.ShouldSave("x" + i));
        Assert.True(d.ShouldSave("x10"));
    }

    [Fact]
    public void ChangeRevertedBeforeSettlingIsNotSaved()
    {
        var d = new SaveDebounce();
        d.ShouldSave("a");
        Assert.False(d.ShouldSave("b"));
        Assert.False(d.ShouldSave("a"));
        Assert.False(d.ShouldSave("a"));
    }

    [Fact]
    public void SecondChangeAfterASaveIsSavedToo()
    {
        var d = new SaveDebounce();
        d.ShouldSave("a");
        d.ShouldSave("b");
        Assert.True(d.ShouldSave("b"));
        Assert.False(d.ShouldSave("c"));
        Assert.True(d.ShouldSave("c"));
    }

    [Fact]
    public void ReturningToAnEarlierSavedValueIsAChange()
    {
        var d = new SaveDebounce();
        d.ShouldSave("a");
        d.ShouldSave("b");
        Assert.True(d.ShouldSave("b"));
        Assert.False(d.ShouldSave("a"));
        Assert.True(d.ShouldSave("a"));
    }
}
