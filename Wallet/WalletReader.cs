using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace GilDelta.Wallet;

public sealed class WalletReader
{
    /// <summary>FFXIV's gil item ID. Constant.</summary>
    private const uint GilItemId = 1;

    /// <summary>
    /// Decides whether a free company chest figure is a balance or a placeholder. Holds
    /// per-session state, so it is cleared by <see cref="ResetSession"/> alongside the
    /// wallet baseline.
    /// </summary>
    private readonly FreeCompanyChestGate _chestGate = new();

    /// <summary>
    /// The free company chest's state on the previous pass, so a change can be logged
    /// once instead of sixty times a second. null = no pass yet.
    /// Touched only from the framework thread, like the rest of this class.
    /// </summary>
    private ChestFigureState? _freeCompanyState;

    /// <summary>
    /// The chest's state as of the last pass; null = not read yet, which includes
    /// "this character has no free company". Framework thread only.
    /// </summary>
    public ChestFigureState? FreeCompanyChestState => _freeCompanyState;

    /// <summary>Filled afresh on every pass. Never handed out.</summary>
    private readonly List<Wallet> _scratch = new();

    /// <summary>The last reading handed out. Replaced, never edited, when a pass differs.</summary>
    private IReadOnlyList<Wallet> _published = Array.Empty<Wallet>();

    private readonly NameMemo _retainerNames = new(slots: 10, capacity: 64);
    private readonly NameMemo _freeCompanyName = new(slots: 1, capacity: 64);

    /// <returns>
    /// This pass's wallets. 🔴 The list is immutable, and it is the very instance the last
    /// pass returned whenever nothing differs, so a caller may keep it across frames.
    /// </returns>
    public IReadOnlyList<Wallet> ReadAll()
    {
        _scratch.Clear();
        TryReadSelf(_scratch);
        TryReadRetainers(_scratch);
        TryReadFreeCompany(_scratch);

        if (!WalletSnapshotStore.SameContents(_published, _scratch))
            _published = _scratch.ToArray();
        return _published;
    }

    /// <summary>
    /// Drops everything this reader remembers across ticks. Called from
    /// <see cref="WalletWatcher.Reset"/>, i.e. at exactly the moments the wallet baseline
    /// is dropped - a character swap, or no character being loaded. The last reading and
    /// the name memos stay: both are re-checked against what the game holds on every pass.
    /// </summary>
    public void ResetSession()
    {
        _chestGate.Reset();
        _freeCompanyState = null;
    }

    private static void TryReadSelf(List<Wallet> sink)
    {
        unsafe
        {
            var manager = InventoryManager.Instance();
            if (manager == null) return;
            var amount = manager->GetInventoryItemCount(GilItemId);
            sink.Add(new Wallet(new WalletId(WalletKind.Self, ""), (long)amount));
        }
    }

    private void TryReadRetainers(List<Wallet> sink)
    {
        unsafe
        {
            var rm = FFXIVClientStructs.FFXIV.Client.Game.RetainerManager.Instance();
            if (rm == null) return;
            for (var i = 0u; i < rm->Retainers.Length; i++)
            {
                var r = rm->Retainers[(int)i];
                if (r.RetainerId == 0) continue;       // empty slot
                if (!_retainerNames.TryGet((int)i, r.Name, out var name))
                {
                    name = r.NameString;
                    _retainerNames.Remember((int)i, r.Name, name);
                }
                if (string.IsNullOrEmpty(name)) continue;
                sink.Add(new Wallet(
                    new WalletId(WalletKind.Retainer, name),
                    (long)r.Gil));
            }
        }
    }

    private void TryReadFreeCompany(List<Wallet> sink)
    {
        unsafe
        {
            string fcName;
            ChestFigure figure;
            try
            {
                var proxy = FFXIVClientStructs.FFXIV.Client.UI.Info.InfoProxyFreeCompany.Instance();
                if (proxy == null) return;
                if (!_freeCompanyName.TryGet(0, proxy->Name, out fcName))
                {
                    fcName = proxy->NameString;
                    _freeCompanyName.Remember(0, proxy->Name, fcName);
                }
                if (string.IsNullOrEmpty(fcName)) return;

                figure = ReadFreeCompanyGil(fcName);
            }
            catch
            {
                // FCS API moved; skip gracefully so the rest of ReadAll keeps working.
                return;
            }

            LogStateChange(figure.State);

            // The row is still added when the amount is unknown: dropping it outright
            // would make the chest vanish from the breakdown and from the account-wide
            // merge every time the player walks out of the company house. A null amount
            // says "we know this chest exists, we just cannot see into it right now",
            // which is what the UI renders as "?".
            sink.Add(new Wallet(new WalletId(WalletKind.FreeCompanyChest, fcName), figure.Amount));
        }
    }

    /// <summary>
    /// The free company chest balance, or a placeholder saying why there isn't one.
    /// </summary>
    private unsafe ChestFigure ReadFreeCompanyGil(string fcName)
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return new ChestFigure(null, ChestFigureState.ContainerNotLoaded);

        var container = manager->GetInventoryContainer(InventoryType.FreeCompanyGil);
        if (container == null) return new ChestFigure(null, ChestFigureState.ContainerNotLoaded);

        return _chestGate.Read(fcName, container->IsLoaded, (long)manager->GetFreeCompanyGil());
    }

    /// <summary>
    /// Logs each change of "can we see the chest, and if not why". Debug rather than
    /// Information: the same fact rides along on the one line a login produces.
    /// </summary>
    private void LogStateChange(ChestFigureState state)
    {
        if (_freeCompanyState == state) return;
        _freeCompanyState = state;
        Service.Log.Debug(
            "GilDelta: free company chest balance is now {State}", Describe(state));
    }

    /// <summary>The parenthetical of the login summary line.</summary>
    public static string DescribeForLogin(ChestFigureState? state) => state switch
    {
        null => "尚未讀到公會儲物櫃",
        ChestFigureState.Readable => "公會儲物櫃可讀",
        ChestFigureState.ContainerNotLoaded =>
            "公會儲物櫃讀不到：金幣容器未載入",
        _ => "公會儲物櫃讀不到：伺服器尚未送出餘額",
    };

    private static string Describe(ChestFigureState state) => state switch
    {
        ChestFigureState.Readable => "readable",
        ChestFigureState.ContainerNotLoaded =>
            "unreadable, gil container not loaded (not counted, shown as ?)",
        _ => "unreadable, the server has not sent this company's balance yet " +
             "(not counted, shown as ?)",
    };
}
