using GilDelta.Localization;

namespace GilDelta.Characters;

/// <summary>
/// One character on the account that the plugin has seen.
/// <see cref="ContentId"/> 0 is the "unknown character" bucket: records that were
/// written before multi-character support existed and that carry no owner we can
/// trust (i.e. they live in <c>events/0.jsonl</c>, written while no character was
/// loaded). Records that live in <c>events/&lt;contentId&gt;.jsonl</c> are attributed
/// to that content id instead — the file name IS a recorded owner, not a guess.
/// </summary>
public sealed record CharacterInfo(ulong ContentId, string Name, string World)
{
    public const ulong UnknownContentId = 0;

    /// <summary>The bucket for records with no trustworthy owner.</summary>
    public static CharacterInfo Unknown { get; } = new(UnknownContentId, string.Empty, string.Empty);

    public bool IsUnknown => ContentId == UnknownContentId;

    /// <summary>
    /// False while the display identity is still missing pieces. Right after login
    /// the name/home-world fields can lag the content id by a few frames, so the
    /// caller keeps re-observing until this flips.
    /// </summary>
    public bool IsComplete => Name.Length > 0 && World.Length > 0;

    /// <summary>
    /// Display label. Never renders a bare "0" — an unknown owner has to *look*
    /// unknown rather than look like a real character named 0.
    /// </summary>
    public string Label =>
        IsUnknown          ? Strings.CharacterUnknown :
        Name.Length == 0   ? $"#{ContentId}" :
        World.Length == 0  ? Name :
                             $"{Name}@{World}";
}
