using System.Linq;
using GilDelta.Characters;
using GilDelta.Localization;

namespace GilDelta.Tests.Characters;

public class CharacterRegistryTests
{
    [Fact]
    public void Observe_adds_a_new_character_and_reports_the_change()
    {
        var registry = new CharacterRegistry();

        Assert.True(registry.Observe(1001, "Alpha", "Chocobo"));
        Assert.Equal(1, registry.Count);
        Assert.Equal("Alpha", registry.Get(1001).Name);
        Assert.Equal("Chocobo", registry.Get(1001).World);
    }

    [Fact]
    public void Observe_reports_no_change_when_nothing_new_arrives()
    {
        var registry = new CharacterRegistry();
        registry.Observe(1001, "Alpha", "Chocobo");

        Assert.False(registry.Observe(1001, "Alpha", "Chocobo"));
    }

    [Fact]
    public void Observe_never_clobbers_a_known_value_with_an_empty_one()
    {
        // Right after login the game can hand back a content id several frames before
        // the name and home world are populated; those empty reads must not wipe what
        // we already know.
        var registry = new CharacterRegistry();
        registry.Observe(1001, "Alpha", "Chocobo");

        Assert.False(registry.Observe(1001, string.Empty, null));
        Assert.Equal("Alpha", registry.Get(1001).Name);
        Assert.Equal("Chocobo", registry.Get(1001).World);
    }

    [Fact]
    public void Observe_fills_in_a_missing_world_later()
    {
        var registry = new CharacterRegistry();
        registry.Observe(1001, "Alpha", string.Empty);
        Assert.False(registry.Get(1001).IsComplete);

        Assert.True(registry.Observe(1001, "Alpha", "Chocobo"));
        Assert.True(registry.Get(1001).IsComplete);
    }

    [Fact]
    public void EnsureKnown_creates_a_placeholder_row_once()
    {
        var registry = new CharacterRegistry();

        Assert.True(registry.EnsureKnown(1001));
        Assert.False(registry.EnsureKnown(1001));
        Assert.True(registry.Contains(1001));
        Assert.False(registry.Get(1001).IsComplete);
    }

    [Fact]
    public void The_unknown_bucket_is_never_stored_as_a_character()
    {
        var registry = new CharacterRegistry();

        Assert.False(registry.Observe(CharacterInfo.UnknownContentId, "Nobody", "Nowhere"));
        Assert.Equal(0, registry.Count);
        Assert.True(registry.Get(CharacterInfo.UnknownContentId).IsUnknown);
    }

    [Fact]
    public void Serialize_round_trips_through_LoadFrom()
    {
        var source = new CharacterRegistry();
        source.Observe(1001, "Alpha", "Chocobo");
        source.Observe(2002, "Beta", "Mandragora");

        var restored = new CharacterRegistry();
        restored.LoadFrom(source.Serialize());

        Assert.Equal(2, restored.Count);
        Assert.Equal("Alpha@Chocobo", restored.Get(1001).Label);
        Assert.Equal("Beta@Mandragora", restored.Get(2002).Label);
    }

    [Fact]
    public void LoadFrom_ignores_garbage_instead_of_throwing()
    {
        var registry = new CharacterRegistry();
        registry.Observe(1001, "Alpha", "Chocobo");

        registry.LoadFrom("this is not json");

        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Label_never_renders_an_unknown_owner_as_a_number()
    {
        Assert.Equal(Strings.CharacterUnknown, CharacterInfo.Unknown.Label);
        Assert.Equal("#1001", new CharacterInfo(1001, string.Empty, string.Empty).Label);
        Assert.Equal("Alpha", new CharacterInfo(1001, "Alpha", string.Empty).Label);
    }

    [Fact]
    public void All_is_ordered_by_content_id()
    {
        var registry = new CharacterRegistry();
        registry.Observe(3003, "Gamma", "Chocobo");
        registry.Observe(1001, "Alpha", "Chocobo");
        registry.Observe(2002, "Beta", "Chocobo");

        Assert.Equal(new ulong[] { 1001, 2002, 3003 }, registry.All.Select(c => c.ContentId).ToArray());
    }
}
