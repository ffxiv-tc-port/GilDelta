namespace GilDelta.Characters;

/// <summary>
/// How a view (dashboard / widget) picks which character's records to show.
/// Persisted as a string in <see cref="Configuration"/> so a content id never has
/// to be squeezed into a signed field.
/// </summary>
/// <remarks>
/// <para><see cref="FollowCurrent"/> ("") is the default and reproduces the v1
/// behaviour exactly: you see the character you are playing.</para>
/// <para><see cref="AllCharacters"/> ("*") aggregates the whole account.</para>
/// <para>Anything else is a literal content id; "0" is the unknown bucket.</para>
/// </remarks>
public static class CharacterScope
{
    public const string FollowCurrent = "";
    public const string AllCharacters = "*";

    public static bool IsAll(string? value) => value == AllCharacters;

    public static bool IsFollowCurrent(string? value) => string.IsNullOrEmpty(value);

    /// <summary>
    /// Resolves a stored scope to a concrete content id.
    /// </summary>
    /// <param name="value">The persisted setting.</param>
    /// <param name="currentContentId">
    /// The character to fall back on — the one being played, or the last one played
    /// when logged out (so logging out does not silently switch the view to the
    /// unknown bucket).
    /// </param>
    /// <returns>null means "every character".</returns>
    public static ulong? Resolve(string? value, ulong currentContentId)
    {
        if (IsAll(value)) return null;
        if (IsFollowCurrent(value)) return currentContentId;
        return ulong.TryParse(value, out var parsed) ? parsed : currentContentId;
    }
}
