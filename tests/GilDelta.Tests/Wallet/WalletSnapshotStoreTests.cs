using System.Collections.Generic;
using System.Linq;
using GilDelta.Wallet;

namespace GilDelta.Tests.Wallet;

/// <summary>
/// The account-wide total: the game only ever exposes the wallets of the character
/// you are logged in as, so the total has to be assembled from the last reading taken
/// for each character.
/// </summary>
public class WalletSnapshotStoreTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-05-05T14:00:00+09:00");

    private static GilDelta.Wallet.Wallet W(WalletKind kind, string id, long amount) =>
        new(new WalletId(kind, id), amount);

    [Fact]
    public void Update_reports_a_change_the_first_time_and_not_again()
    {
        var store = new WalletSnapshotStore();
        var wallets = new[] { W(WalletKind.Self, "", 1000) };

        Assert.True(store.Update(1001, wallets, T0));
        Assert.False(store.Update(1001, wallets, T0.AddMinutes(1)));
    }

    [Fact]
    public void Update_reports_a_change_when_an_amount_moves()
    {
        var store = new WalletSnapshotStore();
        store.Update(1001, new[] { W(WalletKind.Self, "", 1000) }, T0);

        Assert.True(store.Update(1001, new[] { W(WalletKind.Self, "", 1500) }, T0.AddMinutes(1)));
        Assert.Equal(1500, store.TryGet(1001, out var snapshot) ? snapshot.Total : -1);
    }

    [Fact]
    public void Update_copies_the_list_it_is_handed()
    {
        // The caller passes the live snapshot, which is rebuilt every tick; keeping a
        // reference to it would make the stored reading change under our feet.
        var store = new WalletSnapshotStore();
        var live = new List<GilDelta.Wallet.Wallet> { W(WalletKind.Self, "", 1000) };
        store.Update(1001, live, T0);

        live.Clear();

        Assert.True(store.TryGet(1001, out var snapshot));
        Assert.Single(snapshot.Wallets);
    }

    [Fact]
    public void Merge_keeps_every_characters_own_purse()
    {
        // Self carries an empty identifier for every character, so keying by WalletId
        // alone would collapse the whole account into one row.
        var store = new WalletSnapshotStore();
        store.Update(1001, new[] { W(WalletKind.Self, "", 1000) }, T0);
        store.Update(2002, new[] { W(WalletKind.Self, "", 2000) }, T0.AddMinutes(1));

        var merged = WalletSnapshotStore.Merge(store.All);

        Assert.Equal(2, merged.Count);
        Assert.Equal(3000, merged.Sum(w => w.Amount));
    }

    [Fact]
    public void Merge_counts_a_shared_fc_chest_once_using_the_newest_reading()
    {
        var store = new WalletSnapshotStore();
        store.Update(1001, new[]
        {
            W(WalletKind.Self, "", 1000),
            W(WalletKind.FreeCompanyChest, "Guild", 50_000),
        }, T0);
        store.Update(2002, new[]
        {
            W(WalletKind.Self, "", 2000),
            W(WalletKind.FreeCompanyChest, "Guild", 60_000),
        }, T0.AddMinutes(1));

        var merged = WalletSnapshotStore.Merge(store.All);

        Assert.Single(merged, w => w.Id.Kind == WalletKind.FreeCompanyChest);
        Assert.Equal(63_000, merged.Sum(w => w.Amount));
    }

    [Fact]
    public void Merge_keeps_two_different_fc_chests_apart()
    {
        var store = new WalletSnapshotStore();
        store.Update(1001, new[] { W(WalletKind.FreeCompanyChest, "GuildA", 10) }, T0);
        store.Update(2002, new[] { W(WalletKind.FreeCompanyChest, "GuildB", 20) }, T0.AddMinutes(1));

        Assert.Equal(30, WalletSnapshotStore.Merge(store.All).Sum(w => w.Amount));
    }

    [Fact]
    public void OldestAt_is_the_worst_case_staleness()
    {
        var store = new WalletSnapshotStore();
        store.Update(1001, new[] { W(WalletKind.Self, "", 1) }, T0);
        store.Update(2002, new[] { W(WalletKind.Self, "", 2) }, T0.AddDays(3));

        Assert.Equal(T0, WalletSnapshotStore.OldestAt(store.All));
    }

    [Fact]
    public void OldestAt_is_null_when_nothing_has_been_read()
    {
        Assert.Null(WalletSnapshotStore.OldestAt(new WalletSnapshotStore().All));
    }

    [Fact]
    public void Serialize_round_trips_through_LoadFrom()
    {
        var source = new WalletSnapshotStore();
        source.Update(1001, new[]
        {
            W(WalletKind.Self, "", 1000),
            W(WalletKind.Retainer, "Bob", 250),
        }, T0);

        var restored = new WalletSnapshotStore();
        restored.LoadFrom(source.Serialize());

        Assert.True(restored.TryGet(1001, out var snapshot));
        Assert.Equal(1250, snapshot.Total);
        Assert.Equal(T0, snapshot.At);
        Assert.Equal(WalletKind.Retainer, snapshot.Wallets[1].Id.Kind);
        Assert.Equal("Bob", snapshot.Wallets[1].Id.Identifier);
    }

    [Fact]
    public void LoadFrom_ignores_garbage_instead_of_throwing()
    {
        var store = new WalletSnapshotStore();
        store.Update(1001, new[] { W(WalletKind.Self, "", 1000) }, T0);

        store.LoadFrom("not json at all");

        Assert.Equal(1, store.Count);
    }

    // ─── Wallets that could not be read ────────────────────────────────────────

    private static GilDelta.Wallet.Wallet Unknown(WalletKind kind, string id) =>
        new(new WalletId(kind, id), null);

    [Fact]
    public void Total_ignores_wallets_that_could_not_be_read()
    {
        var snapshot = new WalletSnapshot(1001, T0, new[]
        {
            W(WalletKind.Self, "", 1_000),
            Unknown(WalletKind.FreeCompanyChest, "FC"),
        });

        Assert.Equal(1_000, snapshot.Total);
        Assert.True(snapshot.HasUnknown);
    }

    [Fact]
    public void HasUnknown_is_false_when_every_wallet_was_read()
    {
        var snapshot = new WalletSnapshot(1001, T0, new[] { W(WalletKind.Self, "", 1_000) });
        Assert.False(snapshot.HasUnknown);
    }

    [Fact]
    public void Merge_prefers_a_known_chest_reading_over_a_newer_unreadable_one()
    {
        // The character who logged in most recently is the one least likely to have been
        // standing at the chest, so "newest wins" on its own would throw the only real
        // figure on the account away.
        var newer = new WalletSnapshot(1002, T0.AddHours(1), new[]
        {
            W(WalletKind.Self, "", 500),
            Unknown(WalletKind.FreeCompanyChest, "FC"),
        });
        var older = new WalletSnapshot(1001, T0, new[]
        {
            W(WalletKind.Self, "", 1_000),
            W(WalletKind.FreeCompanyChest, "FC", 50_000_000),
        });

        var merged = WalletSnapshotStore.Merge(new[] { newer, older });

        var chest = merged.Where(w => w.Id.Kind == WalletKind.FreeCompanyChest).ToList();
        Assert.Single(chest);
        Assert.Equal(50_000_000, chest[0].Amount);

        // Both characters' own purses survive; only the shared chest is de-duplicated.
        Assert.Equal(2, merged.Count(w => w.Id.Kind == WalletKind.Self));
    }

    [Fact]
    public void Merge_keeps_an_unreadable_chest_row_when_that_is_all_there_is()
    {
        // It must stay in the list, or the view cannot tell "no company" from
        // "company chest we cannot see into" - and would show neither.
        var a = new WalletSnapshot(1001, T0, new[] { Unknown(WalletKind.FreeCompanyChest, "FC") });
        var b = new WalletSnapshot(1002, T0.AddHours(1), new[] { Unknown(WalletKind.FreeCompanyChest, "FC") });

        var merged = WalletSnapshotStore.Merge(new[] { a, b });

        var chest = Assert.Single(merged, w => w.Id.Kind == WalletKind.FreeCompanyChest);
        Assert.Null(chest.Amount);
    }

    [Fact]
    public void Merge_does_not_let_an_older_unreadable_row_displace_a_newer_real_one()
    {
        var newer = new WalletSnapshot(1002, T0.AddHours(1), new[]
        {
            W(WalletKind.FreeCompanyChest, "FC", 42),
        });
        var older = new WalletSnapshot(1001, T0, new[] { Unknown(WalletKind.FreeCompanyChest, "FC") });

        var merged = WalletSnapshotStore.Merge(new[] { newer, older });

        var chest = Assert.Single(merged, w => w.Id.Kind == WalletKind.FreeCompanyChest);
        Assert.Equal(42, chest.Amount);
    }

    [Fact]
    public void An_unreadable_amount_survives_a_save_and_load_round_trip()
    {
        var store = new WalletSnapshotStore();
        store.Update(1001, new[]
        {
            W(WalletKind.Self, "", 1_000),
            Unknown(WalletKind.FreeCompanyChest, "FC"),
        }, T0);

        var json = store.Serialize();

        // 🔴 It must not come back as a real zero - that is the bug, written to disk.
        Assert.DoesNotContain("\"amount\":0", json);

        var reloaded = new WalletSnapshotStore();
        reloaded.LoadFrom(json);

        Assert.True(reloaded.TryGet(1001, out var snapshot));
        var chest = Assert.Single(snapshot.Wallets, w => w.Id.Kind == WalletKind.FreeCompanyChest);
        Assert.Null(chest.Amount);
        Assert.Equal(1_000, snapshot.Total);
    }

    [Fact]
    public void Update_notices_a_wallet_going_from_unreadable_to_a_real_figure()
    {
        var store = new WalletSnapshotStore();
        store.Update(1001, new[] { Unknown(WalletKind.FreeCompanyChest, "FC") }, T0);

        Assert.True(store.Update(1001, new[] { W(WalletKind.FreeCompanyChest, "FC", 7) }, T0.AddMinutes(1)));
    }

    // ─── Readings the game only half answered ──────────────────────
    //
    // WalletReader fetches InventoryManager, RetainerManager and InfoProxyFreeCompany
    // separately and returns early from each when the game has not populated it, so a
    // reading taken in the seconds after login routinely has no retainer rows at all.

    [Fact]
    public void A_reading_with_no_retainer_rows_does_not_drop_the_retainers()
    {
        var store = new WalletSnapshotStore();
        store.Update(1001, new[]
        {
            W(WalletKind.Self, "", 1_000),
            W(WalletKind.Retainer, "Bob", 250),
            W(WalletKind.Retainer, "Eve", 700),
        }, T0);

        // The retainer list has not arrived yet.
        store.Update(1001, new[] { W(WalletKind.Self, "", 1_000) }, T0.AddMinutes(1));

        Assert.True(store.TryGet(1001, out var snapshot));
        Assert.Equal(1_950, snapshot.Total);
        Assert.Equal(2, snapshot.Wallets.Count(w => w.Id.Kind == WalletKind.Retainer));
    }

    [Fact]
    public void A_half_answered_reading_is_not_worth_rewriting_the_file_for()
    {
        var store = new WalletSnapshotStore();
        store.Update(1001, new[]
        {
            W(WalletKind.Self, "", 1_000),
            W(WalletKind.Retainer, "Bob", 250),
        }, T0);

        // Carrying the retainer across reproduces the stored contents exactly, so
        // there is nothing new to write out.
        Assert.False(store.Update(1001, new[] { W(WalletKind.Self, "", 1_000) }, T0.AddMinutes(1)));
    }

    [Fact]
    public void The_free_company_chest_survives_a_reading_that_never_mentions_it()
    {
        // InfoProxyFreeCompany is fetched on its own and gives up on its own; the
        // chest vanishing from one reading is not the company being disbanded.
        var store = new WalletSnapshotStore();
        store.Update(1001, new[]
        {
            W(WalletKind.Self, "", 1_000),
            W(WalletKind.FreeCompanyChest, "Guild", 50_000),
        }, T0);

        store.Update(1001, new[] { W(WalletKind.Self, "", 1_200) }, T0.AddMinutes(1));

        Assert.True(store.TryGet(1001, out var snapshot));
        Assert.Equal(51_200, snapshot.Total);
    }

    [Fact]
    public void A_fresh_row_always_beats_the_one_it_replaces()
    {
        // Carrying forward must never resurrect an old figure over a new one.
        var store = new WalletSnapshotStore();
        store.Update(1001, new[]
        {
            W(WalletKind.Self, "", 1_000),
            W(WalletKind.Retainer, "Bob", 250),
        }, T0);

        store.Update(1001, new[]
        {
            W(WalletKind.Self, "", 9_000),
            W(WalletKind.Retainer, "Bob", 1),
        }, T0.AddMinutes(1));

        Assert.True(store.TryGet(1001, out var snapshot));
        Assert.Equal(9_001, snapshot.Total);
        Assert.Equal(2, snapshot.Wallets.Count);
    }

    [Fact]
    public void Dismissing_one_of_several_retainers_still_removes_it()
    {
        // 🔴 The negative control for the carry-forward. The kind is still
        // answered, just with fewer rows, and that has to be taken at face value -
        // otherwise a wallet could never leave the account total again.
        var store = new WalletSnapshotStore();
        store.Update(1001, new[]
        {
            W(WalletKind.Retainer, "Bob", 250),
            W(WalletKind.Retainer, "Eve", 700),
        }, T0);

        store.Update(1001, new[] { W(WalletKind.Retainer, "Eve", 700) }, T0.AddMinutes(1));

        Assert.True(store.TryGet(1001, out var snapshot));
        Assert.Equal(700, snapshot.Total);
        Assert.Single(snapshot.Wallets);
    }

    [Fact]
    public void An_unreadable_row_still_counts_as_an_answer_for_its_kind()
    {
        // "I can see the chest but not into it" is an answer. It must replace the old
        // figure rather than being treated as a section the game skipped, or the chest
        // could never go back to "?" once it had been read once.
        var store = new WalletSnapshotStore();
        store.Update(1001, new[] { W(WalletKind.FreeCompanyChest, "Guild", 50_000) }, T0);

        store.Update(1001, new[] { Unknown(WalletKind.FreeCompanyChest, "Guild") }, T0.AddMinutes(1));

        Assert.True(store.TryGet(1001, out var snapshot));
        Assert.Single(snapshot.Wallets);
        Assert.Null(snapshot.Wallets[0].Amount);
        Assert.True(snapshot.HasUnknown);
    }

    [Fact]
    public void Carried_rows_do_not_leak_between_characters()
    {
        var store = new WalletSnapshotStore();
        store.Update(1001, new[] { W(WalletKind.Retainer, "Bob", 250) }, T0);

        // A different character whose retainers have never been read: there is nothing
        // to carry forward, and the first character rows must not be borrowed.
        store.Update(2002, new[] { W(WalletKind.Self, "", 5) }, T0.AddMinutes(1));

        Assert.True(store.TryGet(2002, out var snapshot));
        Assert.Single(snapshot.Wallets);
        Assert.Equal(5, snapshot.Total);
    }

    // ─── MergeDetailed: the same merge, keeping where each row came from ────────

    [Fact]
    public void MergeDetailed_produces_exactly_what_Merge_does()
    {
        // 🔴 Merge is a projection of MergeDetailed rather than a second copy of the
        // algorithm. This is the test that notices if anyone ever splits them: the
        // account total is summed over one and the balance list is drawn from the other,
        // so a difference here is a breakdown that does not add up to its own total.
        var snapshots = new[]
        {
            new WalletSnapshot(1002, T0.AddHours(1), new[]
            {
                W(WalletKind.Self, "", 500),
                Unknown(WalletKind.FreeCompanyChest, "FC"),
            }),
            new WalletSnapshot(1001, T0, new[]
            {
                W(WalletKind.Self, "", 1_000),
                W(WalletKind.Retainer, "Bob", 250),
                W(WalletKind.FreeCompanyChest, "FC", 50_000_000),
            }),
        };

        var flat = WalletSnapshotStore.Merge(snapshots);
        var detailed = WalletSnapshotStore.MergeDetailed(snapshots);

        Assert.Equal(flat, detailed.Select(o => o.Wallet).ToList());
    }

    [Fact]
    public void MergeDetailed_says_which_character_each_row_came_from_and_when()
    {
        var snapshots = new[]
        {
            new WalletSnapshot(1001, T0, new[] { W(WalletKind.Self, "", 1_000) }),
            new WalletSnapshot(2002, T0.AddHours(-3), new[] { W(WalletKind.Self, "", 7) }),
        };

        var detailed = WalletSnapshotStore.MergeDetailed(snapshots);

        var mine = Assert.Single(detailed, o => o.Wallet.Amount == 1_000);
        Assert.Equal(1001ul, mine.ContentId);
        Assert.Equal(T0, mine.At);

        var theirs = Assert.Single(detailed, o => o.Wallet.Amount == 7);
        Assert.Equal(2002ul, theirs.ContentId);
        Assert.Equal(T0.AddHours(-3), theirs.At);

        // Nothing is "live" here: MergeDetailed only knows what was stored, and whether
        // a row was read on this frame is decided by the plugin against the reading the
        // watcher just took.
        Assert.All(detailed, o => Assert.False(o.IsLive));
    }

    [Fact]
    public void MergeDetailed_moves_the_origin_with_the_chest_figure_that_wins()
    {
        // The row ends up describing the character who could actually see into the
        // chest, not the one we happened to meet the company through first - otherwise
        // the list would date a real figure by an unrelated character's login.
        var newer = new WalletSnapshot(1002, T0.AddHours(1), new[]
        {
            Unknown(WalletKind.FreeCompanyChest, "FC"),
        });
        var older = new WalletSnapshot(1001, T0, new[]
        {
            W(WalletKind.FreeCompanyChest, "FC", 50_000_000),
        });

        var chest = Assert.Single(
            WalletSnapshotStore.MergeDetailed(new[] { newer, older }),
            o => o.Wallet.Id.Kind == WalletKind.FreeCompanyChest);

        Assert.Equal(50_000_000, chest.Wallet.Amount);
        Assert.Equal(1001ul, chest.ContentId);
        Assert.Equal(T0, chest.At);
    }

    [Fact]
    public void Version_moves_when_a_snapshot_is_replaced_but_not_on_a_timestamp_refresh()
    {
        // The account total is rebuilt only when this moves: a refresh that bumped it would
        // rebuild every frame, a replacement that did not would leave a stale total up.
        var store = new WalletSnapshotStore();
        var wallets = new[] { W(WalletKind.Self, "", 1000) };

        var v0 = store.Version;
        store.Update(1001, wallets, T0);
        var v1 = store.Version;
        Assert.True(v1 > v0);

        store.Update(1001, wallets, T0.AddMinutes(1));
        Assert.Equal(v1, store.Version);

        store.Update(1001, new[] { W(WalletKind.Self, "", 1500) }, T0.AddMinutes(2));
        Assert.True(store.Version > v1);
    }

    [Fact]
    public void Version_moves_when_a_file_is_loaded()
    {
        var source = new WalletSnapshotStore();
        source.Update(1001, new[] { W(WalletKind.Self, "", 5) }, T0);

        var store = new WalletSnapshotStore();
        var before = store.Version;
        store.LoadFrom(source.Serialize());

        Assert.True(store.Version > before);
    }

    [Fact]
    public void A_snapshot_that_was_handed_out_is_never_edited_by_a_later_update()
    {
        // The views keep the list they were given. The store assembles each reading in a
        // buffer it reuses, and that buffer must never be the thing it stores.
        var store = new WalletSnapshotStore();
        store.Update(1001, new[]
        {
            W(WalletKind.Self, "", 1_000),
            W(WalletKind.Retainer, "Bob", 250),
        }, T0);
        Assert.True(store.TryGet(1001, out var first));
        var kept = first.Wallets;
        var copy = kept.ToArray();

        store.Update(1001, new[] { W(WalletKind.Self, "", 5) }, T0.AddMinutes(1));
        store.Update(1001, new[]
        {
            W(WalletKind.Self, "", 6),
            W(WalletKind.Retainer, "Eve", 1),
        }, T0.AddMinutes(2));
        store.Update(2002, new[] { W(WalletKind.Self, "", 7) }, T0.AddMinutes(3));

        Assert.Equal(copy, kept.ToArray());
        Assert.True(store.TryGet(1001, out var latest));
        Assert.NotSame(kept, latest.Wallets);
        Assert.Equal(2, latest.Wallets.Count);
    }

    [Fact]
    public void An_unchanged_reading_leaves_the_stored_snapshot_itself_in_place()
    {
        var store = new WalletSnapshotStore();
        var reading = new[]
        {
            W(WalletKind.Self, "", 1_000),
            W(WalletKind.Retainer, "Bob", 250),
        };
        store.Update(1001, reading, T0);
        Assert.True(store.TryGet(1001, out var first));

        Assert.False(store.Update(1001, reading, T0.AddMinutes(1)));
        // And the same when the retainer row is only there because it was carried.
        Assert.False(store.Update(1001, new[] { W(WalletKind.Self, "", 1_000) }, T0.AddMinutes(2)));

        Assert.True(store.TryGet(1001, out var again));
        Assert.Same(first, again);
        Assert.Equal(T0, again.At);
    }
}
