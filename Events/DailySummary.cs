using System;
using System.Collections.Generic;
using GilDelta.Wallet;

namespace GilDelta.Events;

/// <summary>
/// One local day's settlement for one character: how much gil moved, split by
/// category, plus the wallet total seen at the start and at the end of that day.
/// 🔴 The free-company-chest half is kept in its own buckets rather than merged into
/// the main ones. That is what lets "exclude the FC chest" be answered for a day that
/// is already sealed on disk: the setting can be flipped either way afterwards and
/// both answers stay exact, without ever needing the original events back.
/// </summary>
public sealed class DailySummary
{
    private readonly Dictionary<GilEventCategory, long> _byCategory = new();
    private readonly Dictionary<GilEventCategory, long> _fcByCategory = new();

    public DailySummary(ulong contentId, DateOnly date)
    {
        ContentId = contentId;
        Date = date;
    }

    /// <summary>Owner of these figures; 0 is the unknown-character bucket.</summary>
    public ulong ContentId { get; }

    /// <summary>
    /// The local day these figures belong to. "Local" means the offset each event
    /// carried when it was recorded, which is the wall clock the player saw — not the
    /// machine's current time zone.
    /// </summary>
    public DateOnly Date { get; }

    /// <summary>
    /// True for a row assembled from several characters. Nets and categories add up
    /// across an account; opening and closing totals do not (each character is read at
    /// a different moment), so an aggregate row leaves those null on purpose.
    /// </summary>
    public bool IsAggregate { get; private init; }

    public long Net { get; private set; }
    public long FreeCompanyNet { get; private set; }
    public int EventCount { get; private set; }
    public int FreeCompanyEventCount { get; private set; }

    /// <summary>Wallet total the first time it was read on this day; null = never read.</summary>
    public long? OpenTotal { get; private set; }
    public long? CloseTotal { get; private set; }
    public long? OpenTotalExcludingFreeCompany { get; private set; }
    public long? CloseTotalExcludingFreeCompany { get; private set; }

    /// <summary>Is there anything here at all? An empty row is never written out.</summary>
    public bool IsEmpty => EventCount == 0 && FreeCompanyEventCount == 0 && OpenTotal is null;

    public long NetFor(bool excludeFreeCompanyChest) =>
        excludeFreeCompanyChest ? Net - FreeCompanyNet : Net;

    public int EventCountFor(bool excludeFreeCompanyChest) =>
        excludeFreeCompanyChest ? EventCount - FreeCompanyEventCount : EventCount;

    public long? OpenFor(bool excludeFreeCompanyChest) =>
        excludeFreeCompanyChest ? OpenTotalExcludingFreeCompany : OpenTotal;

    public long? CloseFor(bool excludeFreeCompanyChest) =>
        excludeFreeCompanyChest ? CloseTotalExcludingFreeCompany : CloseTotal;

    /// <summary>
    /// Per-category nets. Excluding the FC chest returns the stored bucket verbatim;
    /// including it merges the two, which is the only case that allocates.
    /// </summary>
    public IReadOnlyDictionary<GilEventCategory, long> CategoryTotals(bool excludeFreeCompanyChest)
    {
        if (excludeFreeCompanyChest || _fcByCategory.Count == 0) return _byCategory;

        var merged = new Dictionary<GilEventCategory, long>(_byCategory);
        foreach (var kv in _fcByCategory)
        {
            merged.TryGetValue(kv.Key, out var prev);
            merged[kv.Key] = prev + kv.Value;
        }
        return merged;
    }

    public void Add(GilEvent ev) => Add(ev.Category, ev.Amount, ev.Wallet.Kind == WalletKind.FreeCompanyChest);

    public void Add(GilEventCategory category, long amount, bool isFreeCompanyChest)
    {
        var bucket = isFreeCompanyChest ? _fcByCategory : _byCategory;
        bucket.TryGetValue(category, out var prev);
        bucket[category] = prev + amount;

        Net += amount;
        EventCount++;
        if (isFreeCompanyChest)
        {
            FreeCompanyNet += amount;
            FreeCompanyEventCount++;
        }
    }

    /// <summary>
    /// Takes one event back out of the day, exactly reversing <see cref="Add(GilEvent)"/>.
    /// 🔴 Call it once per event, and only when the event was actually booked into this
    /// day. It is deliberately not clamped: a count that has gone negative is a bug worth
    /// seeing, not one worth papering over with a floor.
    /// </summary>
    public void Retract(GilEvent ev) =>
        Retract(ev.Category, ev.Amount, ev.Wallet.Kind == WalletKind.FreeCompanyChest);

    public void Retract(GilEventCategory category, long amount, bool isFreeCompanyChest)
    {
        var bucket = isFreeCompanyChest ? _fcByCategory : _byCategory;
        bucket.TryGetValue(category, out var prev);

        // A category that has gone back to zero is removed rather than left as a 0 row:
        // "no repairs that day" and "repairs that came to nothing" are different claims,
        // and the breakdown would print the second one.
        var left = prev - amount;
        if (left == 0) bucket.Remove(category);
        else bucket[category] = left;

        Net -= amount;
        EventCount--;
        if (isFreeCompanyChest)
        {
            FreeCompanyNet -= amount;
            FreeCompanyEventCount--;
        }
    }

    /// <summary>
    /// Records a wallet reading taken on this day. The first one of the day becomes the
    /// opening total and every one after it moves the closing total.
    /// </summary>
    /// <returns>true when something actually changed, i.e. the file is worth rewriting.</returns>
    public bool ObserveTotal(long total, long totalExcludingFreeCompany)
    {
        var changed = false;

        if (OpenTotal is null)
        {
            OpenTotal = total;
            OpenTotalExcludingFreeCompany = totalExcludingFreeCompany;
            changed = true;
        }

        if (CloseTotal != total || CloseTotalExcludingFreeCompany != totalExcludingFreeCompany)
        {
            CloseTotal = total;
            CloseTotalExcludingFreeCompany = totalExcludingFreeCompany;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Throws away everything derived from events, keeping the wallet readings.
    /// Used when the current day is recomputed from the event log at startup: the log
    /// is the authority for a day that is still running, and this is what heals the
    /// gap if the game went down between an event being appended and the settlement
    /// being written out.
    /// </summary>
    public void ResetEvents()
    {
        _byCategory.Clear();
        _fcByCategory.Clear();
        Net = 0;
        FreeCompanyNet = 0;
        EventCount = 0;
        FreeCompanyEventCount = 0;
    }

    /// <summary>Adds another character's figures for the same day into an aggregate row.</summary>
    private void AddFrom(DailySummary other)
    {
        foreach (var kv in other._byCategory)
        {
            _byCategory.TryGetValue(kv.Key, out var prev);
            _byCategory[kv.Key] = prev + kv.Value;
        }
        foreach (var kv in other._fcByCategory)
        {
            _fcByCategory.TryGetValue(kv.Key, out var prev);
            _fcByCategory[kv.Key] = prev + kv.Value;
        }

        Net += other.Net;
        FreeCompanyNet += other.FreeCompanyNet;
        EventCount += other.EventCount;
        FreeCompanyEventCount += other.FreeCompanyEventCount;
    }

    /// <summary>
    /// Folds one day's rows from several characters into a single row. Opening and
    /// closing totals are deliberately left null - see <see cref="IsAggregate"/>.
    /// </summary>
    public static DailySummary Aggregate(DateOnly date, IEnumerable<DailySummary> rows)
    {
        var merged = new DailySummary(0, date) { IsAggregate = true };
        foreach (var row in rows) merged.AddFrom(row);
        return merged;
    }

    /// <summary>Rebuilds a row from persisted values. Used only by the store.</summary>
    public static DailySummary Restore(
        ulong contentId,
        DateOnly date,
        IEnumerable<KeyValuePair<GilEventCategory, long>> byCategory,
        int eventCount,
        IEnumerable<KeyValuePair<GilEventCategory, long>> freeCompanyByCategory,
        int freeCompanyEventCount,
        long? openTotal,
        long? closeTotal,
        long? openTotalExcludingFreeCompany,
        long? closeTotalExcludingFreeCompany)
    {
        var row = new DailySummary(contentId, date);

        foreach (var kv in byCategory)
        {
            row._byCategory[kv.Key] = kv.Value;
            row.Net += kv.Value;
        }
        foreach (var kv in freeCompanyByCategory)
        {
            row._fcByCategory[kv.Key] = kv.Value;
            row.Net += kv.Value;
            row.FreeCompanyNet += kv.Value;
        }

        // Counts are stored rather than derived: several events can share a category
        // on the same day, so the bucket count is not the event count.
        // eventCount is the TOTAL for the day, freeCompanyEventCount the FC subset of
        // it - the same relationship Add() maintains.
        row.EventCount = eventCount;
        row.FreeCompanyEventCount = freeCompanyEventCount;

        row.OpenTotal = openTotal;
        row.CloseTotal = closeTotal;
        row.OpenTotalExcludingFreeCompany = openTotalExcludingFreeCompany;
        row.CloseTotalExcludingFreeCompany = closeTotalExcludingFreeCompany;
        return row;
    }

    /// <summary>The non-FC category buckets, for the store.</summary>
    public IReadOnlyDictionary<GilEventCategory, long> RawCategoryTotals => _byCategory;

    /// <summary>The FC-chest category buckets, for the store.</summary>
    public IReadOnlyDictionary<GilEventCategory, long> RawFreeCompanyCategoryTotals => _fcByCategory;
}
