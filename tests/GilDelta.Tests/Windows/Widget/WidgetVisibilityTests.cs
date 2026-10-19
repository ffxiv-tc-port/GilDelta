using GilDelta.Windows.Widget;

namespace GilDelta.Tests.Windows.Widget;

public class WidgetVisibilityTests
{
    [Fact]
    public void Draws_when_neither_state_applies()
    {
        Assert.True(WidgetVisibility.ShouldDraw(
            new WidgetHideSettings(InCutscene: true, InDuty: true),
            new WidgetGameState(InCutscene: false, InDuty: false)));
    }

    [Fact]
    public void Hides_in_a_cutscene_when_asked_to()
    {
        Assert.False(WidgetVisibility.ShouldDraw(
            new WidgetHideSettings(InCutscene: true, InDuty: false),
            new WidgetGameState(InCutscene: true, InDuty: false)));
    }

    [Fact]
    public void Hides_in_a_duty_when_asked_to()
    {
        Assert.False(WidgetVisibility.ShouldDraw(
            new WidgetHideSettings(InCutscene: false, InDuty: true),
            new WidgetGameState(InCutscene: false, InDuty: true)));
    }

    // The half that matters most: a state the user did NOT ask to hide for must not
    // hide the widget just because the other checkbox is ticked. Each setting answers
    // for its own flag only.
    [Fact]
    public void Cutscene_alone_does_not_hide_when_only_duty_is_ticked()
    {
        Assert.True(WidgetVisibility.ShouldDraw(
            new WidgetHideSettings(InCutscene: false, InDuty: true),
            new WidgetGameState(InCutscene: true, InDuty: false)));
    }

    [Fact]
    public void Duty_alone_does_not_hide_when_only_cutscene_is_ticked()
    {
        Assert.True(WidgetVisibility.ShouldDraw(
            new WidgetHideSettings(InCutscene: true, InDuty: false),
            new WidgetGameState(InCutscene: false, InDuty: true)));
    }

    [Fact]
    public void Both_settings_off_never_hides()
    {
        var hide = new WidgetHideSettings(InCutscene: false, InDuty: false);
        Assert.True(WidgetVisibility.ShouldDraw(hide, new WidgetGameState(true, true)));
        Assert.True(WidgetVisibility.ShouldDraw(hide, new WidgetGameState(true, false)));
        Assert.True(WidgetVisibility.ShouldDraw(hide, new WidgetGameState(false, true)));
    }

    // A cutscene inside a duty is one frame with both flags up; either ticked box is
    // enough on its own.
    [Fact]
    public void Either_setting_is_enough_when_both_states_are_up()
    {
        Assert.False(WidgetVisibility.ShouldDraw(
            new WidgetHideSettings(InCutscene: true, InDuty: false),
            new WidgetGameState(InCutscene: true, InDuty: true)));
        Assert.False(WidgetVisibility.ShouldDraw(
            new WidgetHideSettings(InCutscene: false, InDuty: true),
            new WidgetGameState(InCutscene: true, InDuty: true)));
    }

    // The shipped defaults are HideInCutscene on, HideInDuty off. Configuration itself
    // cannot be constructed here (it implements Dalamud's IPluginConfiguration and this
    // project has no Dalamud reference), so the pair is spelled out; Configuration.cs is
    // the one place that has to keep matching it.
    [Fact]
    public void Default_settings_hide_in_cutscenes_only()
    {
        var hide = new WidgetHideSettings(InCutscene: true, InDuty: false);
        Assert.False(WidgetVisibility.ShouldDraw(hide, new WidgetGameState(InCutscene: true, InDuty: false)));
        Assert.True(WidgetVisibility.ShouldDraw(hide, new WidgetGameState(InCutscene: false, InDuty: true)));
    }
}
