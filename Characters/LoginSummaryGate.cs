using System.Collections.Generic;

namespace GilDelta.Characters;

/// <summary>
/// Remembers the last login summary written per character, so a character re-entered
/// by automation again and again only gets an Information line when its summary changes.
/// Framework thread only.
/// </summary>
public sealed class LoginSummaryGate
{
    private readonly Dictionary<ulong, string> _last = new();

    /// <summary>True the first time a character logs in, and whenever its summary differs from the last one.</summary>
    public bool IsNew(ulong contentId, string summary)
    {
        if (_last.TryGetValue(contentId, out var previous) && previous == summary) return false;
        _last[contentId] = summary;
        return true;
    }
}
