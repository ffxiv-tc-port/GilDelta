using System.Collections.Generic;
using GilDelta.Wallet;

namespace GilDelta.Events;

/// <summary>
/// What a view is allowed to count: every record except the ones the user excluded and,
/// when the setting is on, the free company chest. Kept between rebuilds; draw thread only.
/// </summary>
/// <remarks>
/// While the log only grows, the new records are folded into the list handed out last time
/// instead of filtering the whole log again. A rewrite of the log, a different source list or a
/// different setting starts over. Returns the source itself while nothing has been dropped.
/// </remarks>
public sealed class VisibleEvents
{
    private IReadOnlyList<GilEvent>? _source;
    private long _rewriteVersion;
    private bool _excludeFreeCompanyChest;
    private int _seen;
    private List<GilEvent>? _kept;

    /// <param name="rewriteVersion"><see cref="EventLog.RewriteVersion"/>: anything but an append bumps it.</param>
    public IReadOnlyList<GilEvent> Get(
        IReadOnlyList<GilEvent> source, long rewriteVersion, bool excludeFreeCompanyChest)
    {
        if (!ReferenceEquals(source, _source) || rewriteVersion != _rewriteVersion ||
            excludeFreeCompanyChest != _excludeFreeCompanyChest || source.Count < _seen)
        {
            _source = source;
            _rewriteVersion = rewriteVersion;
            _excludeFreeCompanyChest = excludeFreeCompanyChest;
            _seen = 0;
            _kept = null;
        }

        for (var i = _seen; i < source.Count; i++)
        {
            var ev = source[i];
            if (_kept is null)
            {
                if (Counts(ev, excludeFreeCompanyChest)) continue;

                // First drop: everything before it was kept.
                _kept = new List<GilEvent>(source.Count);
                for (var k = 0; k < i; k++) _kept.Add(source[k]);
                continue;
            }

            if (Counts(ev, excludeFreeCompanyChest)) _kept.Add(ev);
        }

        _seen = source.Count;
        return _kept ?? source;
    }

    public static bool Counts(GilEvent ev, bool excludeFreeCompanyChest) =>
        !ev.Suspect && !WalletExclusion.IsExcluded(ev.Wallet.Kind, excludeFreeCompanyChest);
}
