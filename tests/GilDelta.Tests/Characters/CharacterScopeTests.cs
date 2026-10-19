using GilDelta.Characters;

namespace GilDelta.Tests.Characters;

public class CharacterScopeTests
{
    [Fact]
    public void The_default_scope_follows_the_current_character()
    {
        // "" is what a fresh Configuration carries, and it has to reproduce v1
        // behaviour exactly: you see the character you are playing.
        Assert.Equal(1001ul, CharacterScope.Resolve(CharacterScope.FollowCurrent, 1001));
    }

    [Fact]
    public void The_all_scope_resolves_to_no_particular_character()
    {
        Assert.Null(CharacterScope.Resolve(CharacterScope.AllCharacters, 1001));
    }

    [Fact]
    public void An_explicit_content_id_wins_over_the_current_character()
    {
        Assert.Equal(2002ul, CharacterScope.Resolve("2002", 1001));
    }

    [Fact]
    public void The_unknown_bucket_is_selectable_by_id()
    {
        Assert.Equal(0ul, CharacterScope.Resolve("0", 1001));
    }

    [Fact]
    public void Junk_falls_back_to_the_current_character()
    {
        Assert.Equal(1001ul, CharacterScope.Resolve("not-an-id", 1001));
        Assert.Equal(1001ul, CharacterScope.Resolve(null, 1001));
    }
}
