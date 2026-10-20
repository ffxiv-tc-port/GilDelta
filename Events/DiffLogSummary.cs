using System;
using System.Globalization;
using System.Text;

namespace GilDelta.Events;

/// <summary>
/// The Information-level trace of gil changes: one line per character per window, with
/// the count and net per category. The per-change line (addons, hints) is Debug.
/// Framework thread only. Counters are fixed arrays sized by the enum, so adding a
/// change allocates nothing; the only allocation is formatting a closed window.
/// </summary>
public sealed class DiffLogSummary
{
    /// <summary>Offline log readers match this text; keep it stable.</summary>
    public const string Prefix = "GilDelta: 金幣變動彙總";

    private static readonly int CategoryCount = Enum.GetValues<GilEventCategory>().Length;

    private readonly int[] _count = new int[CategoryCount];
    private readonly long[] _net = new long[CategoryCount];
    private readonly TimeSpan _window;
    private ulong _owner;
    private DateTimeOffset _start;
    private int _total;
    private long _totalNet;

    public DiffLogSummary(TimeSpan window) => _window = window;

    public bool HasPending => _total > 0;

    /// <summary>
    /// Counts one change. A change for a different character than the open window
    /// closes that window first; its line is returned so it can be logged before this one counts.
    /// </summary>
    public string? Add(ulong owner, GilEventCategory category, long delta, DateTimeOffset at)
    {
        string? closed = null;
        if (_total > 0 && owner != _owner) closed = Take(at);
        if (_total == 0)
        {
            _owner = owner;
            _start = at;
        }

        var i = (int)category;
        if ((uint)i >= (uint)CategoryCount) i = (int)GilEventCategory.Misc;
        _count[i]++;
        _net[i] += delta;
        _total++;
        _totalNet += delta;
        return closed;
    }

    public bool IsDue(DateTimeOffset now) => _total > 0 && now - _start >= _window;

    /// <summary>Formats and clears the open window; null when nothing is pending.</summary>
    public string? Take(DateTimeOffset now)
    {
        if (_total == 0) return null;

        var seconds = Math.Max(0, (long)(now - _start).TotalSeconds);
        var sb = new StringBuilder(Prefix);
        sb.Append(CultureInfo.InvariantCulture,
            $" cid={_owner}，{seconds} 秒內 {_total} 筆，淨額 {Signed(_totalNet)}；");
        var first = true;
        for (var i = 0; i < CategoryCount; i++)
        {
            if (_count[i] == 0) continue;
            if (!first) sb.Append('、');
            first = false;
            sb.Append(CultureInfo.InvariantCulture,
                $"{(GilEventCategory)i}×{_count[i]} {Signed(_net[i])}");
        }

        Array.Clear(_count);
        Array.Clear(_net);
        _total = 0;
        _totalNet = 0;
        return sb.ToString();
    }

    private static string Signed(long value) =>
        value.ToString("+#,0;-#,0;0", CultureInfo.InvariantCulture);
}
