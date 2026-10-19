# CLAUDE.md

> ## 🔴 這個分支是台服移植版（`tc-7.20`）
>
> 下面整份文件是**上游 `main`（API 15 / .NET 10）**留下的筆記。本分支已降到
> **Dalamud API 13 / .NET 9（`Dalamud.NET.Sdk/13.0.0`）**，釘 `dalamud-pin-v13.0.0.16`。
> 移植時**沒有改動任何 C# 原始碼** —— 這顆 pin 已經有 `IPlayerState.ContentId`、
> `Dalamud.Bindings.ImGui`、非泛型的 `IDalamudPluginInterface.Inject(object, …)`、
> `PlotLines(ReadOnlySpan<float>, …)` 與 `IClientState.Logout(int, int)`，
> 所以下面「SDK 15 gotchas」第 1 / 2 / 3 / 4 / 9 條在本分支**同樣成立**。
>
> 改掉的只有專案檔層面：
> - `GilDelta.csproj` → SDK 13；版號改成艦隊慣例 `7.20.0.<BuildNumber>`（`BuildNumber.txt` 自動遞增）。
> - `tests/GilDelta.Tests` → `net9.0-windows7.0`（移植當下 60 個測試全過；本分支目前 314 個）。
> - `GilDelta.json` → `RepoUrl` 指向本 fork；移除 `IconUrl`（feed 端 `mirror_releases.py` 會改寫成
>   `DalamudPluginsTC/icons/GilDelta.png`）；`Changelog` 清空。
> - `.github/workflows/` → 換成艦隊的 `release.yml`（workflow_dispatch）＋ `build-check.yml`。
>
> 建置指令（本機）：
> `dotnet build -c Release -p:DalamudLibPath=D:/ffxiv-tc-port/Dalamud/bin/Release/`
> （🔴 尾端斜線不能省。）
>
> ✅ 台服在地化已完成（本節原本寫「尚未處理」，那個敘述已經過期）：`ClientLanguage` 在台服回 `7`，
> `Plugin.ResolveLanguage` 已改成**用數值比對**，把 `5 or 7` 都對到 `Language.ChineseTraditional`
> （不要改回用列舉名比對：全球版客戶端的 `ChineseTraditional` 是 `5`，只認名字會漏掉台服的 `7`）；
> `Localization/Strings.cs` 的 `T()` 是 **7 個語言參數**（EN／JA／DE／FR／ZH／KO／TW），全檔都有繁中。

Notes for future agents working on GilDelta.

## Project at a glance

- **Type:** Dalamud plugin (FFXIV). Upstream `main`: API 15 / `Dalamud.NET.Sdk/15.0.0`. **This `tc-7.20` branch: API 13 / `Dalamud.NET.Sdk/13.0.0` / .NET 9.**
- **Goal:** read-only gil tracker — observe gil changes across self / retainers / FC chest, classify each into a category (`SubmarineReturn`, `RetainerSale`, `NpcShopBuy`, etc.), persist as JSONL, surface through a configurable always-on widget plus a five-tab dashboard.
- **Read-only invariant.** No game-state writes anywhere. WalletReader only reads from FFXIVClientStructs; if a future task needs to call any "Execute"/"Send"/"Set" API, stop and confirm scope with the user.

## Code map

```
GilDelta/
├── GilDelta.csproj                    # Dalamud.NET.Sdk/13.0.0 on this branch (15.0.0 upstream); pins output to bin/Release/
├── GilDelta.json                      # plugin manifest shown in Dalamud
├── GilDelta.sln                       # main project + tests/GilDelta.Tests
├── Plugin.cs                          # IDalamudPlugin entry: fields, constructor (DI, command registration), Dispose
├── Plugin.Persistence.cs              # partial: startup load, daily settlement files, retention / event-file rewrites
├── Plugin.FrameworkTick.cs            # partial: per-frame tick, active-character tracking, wallet snapshot cache
├── Plugin.GilEvents.cs                # partial: login/logout/command handlers, HandleDiff, transfer relabel, exclude/restore
├── Plugin.Views.cs                    # partial: widget / dashboard context composition and their caches
├── Service.cs                         # [PluginService] container (PluginInterface, Framework, ClientState, GameGui, CommandManager, Log, Condition, PlayerState)
├── Configuration.cs                   # IPluginConfiguration; note AddonRecencySeconds (default 10)
├── Characters/
│   ├── CharacterInfo.cs               # record (ContentId + Name + World); ContentId 0 = unknown bucket
│   ├── CharacterRegistry.cs           # roster persisted to characters.json
│   └── CharacterScope.cs              # "" = follow current, "*" = all, else a content id
├── Storage/
│   ├── AtomicFile.cs                  # temp file + rename, unique temp name
│   └── SerialJsonWriter.cs            # last-write-wins background writer, no lock across I/O
├── Wallet/
│   ├── WalletKind.cs                  # enum: Self / Retainer / FreeCompanyChest
│   ├── WalletId.cs                    # readonly record struct (Kind + Identifier)
│   ├── Wallet.cs                      # readonly record struct (Id + Amount); Amount is long? — null = COULD NOT READ
│   ├── WalletDiff.cs                  # readonly record struct, computes Delta (Before/After are non-null by construction)
│   ├── WalletReader.cs                # reads via FFXIVClientStructs (see below)
│   ├── WalletBaseline.cs              # last known balances; the ONLY place that decides what becomes an event
│   ├── WalletWatcher.cs               # Framework.Update poller, emits OnDiff; isEnabled gate + Reset()
│   ├── WalletSnapshot.cs              # one character's last known wallets + when they were read
│   ├── WalletSnapshotStore.cs         # per-character cache behind the account-wide total; Merge is a projection of MergeDetailed
│   ├── WalletOrigin.cs                # a wallet + which character read it, when, and whether it is this frame
│   ├── AddonProbe.cs                  # checks which Watched addons are loaded
│   └── AddonStateTracker.cs           # rolling history of seen addons (window = Configuration.AddonRecencySeconds)
├── Ipc/
│   └── GilDeltaIpc.cs                 # provider side; registers GilDelta.Hint
├── Events/
│   ├── GilEventCategory.cs            # 11-member enum, persisted BY NAME (see the doc comment)
│   ├── GilEvent.cs                    # sealed record (Timestamp, Wallet, Amount, Category, Note)
│   ├── GilHint.cs                     # one IPC hint (Category + Note + ExpiresAt)
│   ├── IHintSource.cs                 # the destructive TryConsume the rules see
│   ├── GilHintStore.cs                # IPC inbox (caller thread) + active shelf (framework thread)
│   ├── HintScope.cs                   # which wallet kind / direction a category may claim
│   ├── GameContext.cs                 # OpenAddons (+ optional AddonLastSeen ages) + RecentDiffs + Now + Hints, fed to rules
│   ├── IInferenceRule.cs              # TryClassify(diff, ctx, [NotNullWhen(true)] out ev)
│   ├── Inferrer.cs                    # priority-ordered rule chain; a throwing rule is skipped, and warned about once
│   ├── RuleChain.cs                   # the one ordered rule list; Plugin.cs and RuleChainTests both build from it
│   ├── EventStore.cs                  # JSONL persistence with v=1 schema, Reclassify with .bak rollback
│   ├── EventStoreSet.cs               # one EventStore per character under events/
│   ├── EventLog.cs                    # in-memory cache exposed to Windows
│   ├── DailySummary.cs                # one local day's settlement; FC half kept in its own buckets
│   ├── DailyLedger.cs                 # the settlement book, JSONL under daily/<contentId>.jsonl
│   ├── WalletExclusion.cs             # "leave the FC chest out" filter; keys on wallet KIND not category
│   └── Rules/
│       ├── PairedTransferRule.cs      # Self↔Retainer paired diffs within 500ms → Deposit/Withdraw
│       ├── HintRule.cs                # consumes a GilDelta.Hint from another plugin
│       ├── FreeCompanyChestDepositRule.cs # FC chest +Δ → FreeCompanyChestDeposit (SubmarineReturn needs a hint)
│       ├── RetainerSaleRule.cs        # Retainer +Δ, or Self +Δ near a retainer window → RetainerSale
│       ├── NpcShopBuyRule.cs          # Self -Δ + Shop/InclusionShop addon
│       ├── NpcShopSellRule.cs         # Self +Δ + Shop/InclusionShop addon, or the retainer sell list without the bell menu (retainer vendor sale)
│       ├── MarketBoardBuyRule.cs      # Self -Δ + ItemSearch addon
│       ├── RepairRule.cs              # Self -Δ + Repair addon
│       ├── TeleportRule.cs            # Self -Δ + Teleport addon; below every shop/repair/retainer rule, and yields to a letter editor seen later
│       ├── CharacterTransferRule.cs   # equal+opposite change on another of the account's characters
│       ├── MailRule.cs                # Self ±Δ + LetterViewer/LetterEditor; below CharacterTransferRule
│       └── MiscRule.cs                # fallback (always matches)
├── Theme/                             # MidnightCoin (dark + gold + monospace)
├── Localization/                      # 7-arg T() helper (EN/JA/DE/FR/ZH/KO/TW)
├── Windows/
│   ├── ConfigWindow.cs                # General (incl. FC-chest exclusion) / Widget / Dashboard / Data
│   ├── ViewCache.cs                   # a built widget/dashboard context + the ViewInputs it was built from
│   ├── Widget/
│   │   ├── WidgetContext.cs           # totals + per-source breakdown + today/week/month; Wallets is DERIVED from Origins
│   │   ├── TotalHotspot.cs            # makes the total clickable in every density (-> dashboard, Daily tab)
│   │   ├── IWidgetRenderer.cs         # interface keyed by WidgetDensity
│   │   ├── WidgetWindow.cs            # always-on, position-persistent; PreOpenCheck keeps "Show widget" live
│   │   ├── WidgetVisibility.cs        # answers "Hide in cutscene"/"Hide in duty"; ImGui-free, so it is testable
│   │   ├── MinimalRenderer.cs         # one-line pill
│   │   ├── CompactRenderer.cs         # default; total + delta + breakdown
│   │   ├── TrendRenderer.cs           # Compact + 24h sparkline
│   │   └── TileRenderer.cs            # today / week / month tile grid
│   └── Dashboard/
│       ├── DashboardContext.cs        # daily/weekly bucketing + NetByCategory
│       ├── IDashboardTab.cs           # interface keyed by DashboardTab
│       ├── DashboardWindow.cs         # tab bar; honors DefaultTab on first open, or ShowTab() from a caller
│       ├── TimelineTab.cs             # last 200 events, reverse chronological
│       ├── ChartTab.cs                # hand-drawn wealth line + daily-net bars (NOT PlotLines)
│       ├── ChartScale.cs              # the chart's two arithmetic decisions, ImGui-free so it is testable
│       ├── DailyTab.cs                # one row per settled local day
│       ├── BreakdownTab.cs            # category bars with today/7d/30d/all selector
│       ├── HeatmapTab.cs              # 12-week × 7-day grid, color = daily net
│       ├── BalanceRows.cs             # orders the wallets behind the total; pure, so "the rows add up" is testable
│       ├── BalanceRowSet.cs           # the tab's rows with and without the 0-balance wallets (null is never 0); cached per wallet-list instance
│       ├── BalanceSort.cs             # balance-header click cycle: most first → least first → original; the SETTING is the authority, ImGui's table state is synced to it
│       └── BalanceTab.cs              # the balance list (click the total, or /gildelta wallets); own columns, NOT DetailWindow
├── images/
│   ├── icon.svg                       # source design (G monogram on a gold coin)
│   └── icon.png                       # 512×512 deliverable; needs sRGB+gAMA+pHYs chunks
├── tests/GilDelta.Tests/              # xUnit; pure-logic only (no Dalamud refs)
└── .github/workflows/release.yml      # auto-attaches latest.zip on release publish
```

## Game-side wiring (FFXIVClientStructs APIs we use)

These were verified by decompiling `%AppData%\XIVLauncher\addon\Hooks\dev\FFXIVClientStructs.dll`. If the SDK is bumped and one breaks, re-derive from there.

- **Self gil:** `InventoryManager.Instance()->GetInventoryItemCount(itemId: 1)` returns `int`. Item ID 1 is gil.
- **Retainer gil:** iterate `RetainerManager.Instance()->Retainers` (Span<Retainer>). Skip slots where `RetainerId == 0`. Read `Retainer.Gil` (uint) and `Retainer.NameString` (string).
- **FC chest gil:** `InventoryManager.Instance()->GetFreeCompanyGil()` returns `uint`. 🔴 **It answers a flat `0` when the chest contents have not been sent by the server** — i.e. everywhere except at the chest itself — and that 0 is indistinguishable from a genuinely empty chest. The decision lives in `Wallet/FreeCompanyChestGate.cs`; call that, never `GetFreeCompanyGil()` raw.
  🔴 **The container's own `IsLoaded` flag is NOT a usable gate here, measured not guessed.** It is true from the first tick after login while the figure is still 0: across 26 live log files, all 12 readability transitions the plugin ever logged read "readable" and not one read "unreadable". So the flag rejected nothing, and the fabricated `FreeCompanyChest +50,000,000` deposit happened twice more after that gate shipped (2026-09-10T01:55:36 and 2026-09-10T20:43:11) — both with the chest window open, which also rules out gating on the `FreeCompanyChest` addon: the balance arrives a moment after the window does.
  🔑 **What does discriminate:** a 0 is trustworthy only once the server has sent a non-zero figure for that company in this session, because a non-zero figure is the proof it will speak about it at all. The flag is still checked — it is free and it is read off the same object as the figure — it is just not load-bearing. The memory is session-scoped and cleared by `WalletWatcher.Reset()`; keeping it across a character swap would hand the next character's placeholder 0 the same trust.
  🔴 **This was not always so.** This file used to say the 0 "is fine — a 0-amount wallet still appears in the breakdown row". It was not fine: a user's log gained a fabricated `FreeCompanyChest +50,000,000` deposit the moment they walked into their company house, because `WalletWatcher` diffed the unreadable 0 against the real balance. `Wallet.Amount` is `long?` today precisely so this cannot be expressed any more.
- **FC name:** `InfoProxyFreeCompany.Instance()->NameString`.
- **Open addons:** `Service.GameGui.GetAddonByName(name)` returns `AtkUnitBasePtr`; `addon.Address != 0` means loaded.

## Dalamud SDK 15 gotchas (caught the hard way)

These are the deviations that cost hours during Plan 1 / 2 / 3:

1. **`pi.Inject<T>()` doesn't exist.** Use `pi.Inject(new Service())` (non-generic, takes an instance whose `[PluginService]` static members it fills via reflection). The instance is throwaway because all properties are static.
2. **`IClientState.LocalContentId` is gone.** The active character's content ID lives on `IPlayerState.ContentId`. Service container has both `ClientState` and `PlayerState`.
3. **`Dalamud.Bindings.ImGui` is the active ImGui namespace, NOT `ImGuiNET`.** Includes `ImGui`, `ImGuiCol`, `ImGuiWindowFlags`, `ImGuiTabItemFlags`, etc.
4. **`PlotLines` / `PlotHistogram` signatures differ from ImGui.NET.** Drop the `values_offset` arg and use `ReadOnlySpan<float>`. The working signature is:
   ```
   PlotLines(string label, ReadOnlySpan<float> values, int count,
             string overlay, float scale_min, float scale_max,
             Vector2 size, int stride)
   ```
5. **Default build output is `bin/x64/Release/`** (SDK 15 inserts an `x64` platform segment). Pin it back to `bin/Release/` via the csproj PropertyGroup:
   ```xml
   <PlatformTarget>x64</PlatformTarget>
   <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
   <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
   <OutputPath>bin\$(Configuration)\</OutputPath>
   ```
6. **DalamudPackager 15 cleans up loose files after zipping.** The `DefaultDalamudPackagerRelease` target stages files into `bin/Release/GilDelta/`, zips them into `latest.zip`, then deletes the staged copies. Dev Plugin Locations need a real DLL on disk, so we re-extract the zip in an `ExtractStagedZipForDevPlugin` target wired with `AfterTargets="Build"` AND `DependsOnTargets="DefaultDalamudPackagerDebug;DefaultDalamudPackagerRelease"`. The `DependsOnTargets` is what forces ordering — `AfterTargets="DefaultDalamudPackagerRelease"` alone doesn't.
7. **Plugin icon PNG needs sRGB + gAMA + pHYs chunks.** Pillow's default output (only IHDR/IDAT/IEND) shows up as a blank icon in the plugin installer. Inject the three metadata chunks after IHDR. The Python recipe is in git history (commit `a21b837`).
8. **`Wallet` type vs `GilDelta.Wallet` namespace shadow.** Inside any consumer that does `using GilDelta.Wallet;`, the bare name `Wallet` resolves to the namespace, not the type. Test code under `GilDelta.Tests.Windows.*` and any production code under `GilDelta.Windows.*` must qualify as `Wallet.Wallet` or `GilDelta.Wallet.Wallet`.
9. **`IClientState.Logout` event signature.** `(int type, int code)` — both ints. Don't pass an `EventHandler`.

## Inference chain & diagnostic logging

`Plugin.HandleDiff` runs every wallet diff through `Inferrer.Classify(diff, ctx)` where `ctx.OpenAddons` is the **rolling union** of addons seen within `Configuration.AddonRecencySeconds` (default 10, clamped 1..60) — not just "open right now". This catches the "Shop closed before WalletWatcher detected the next-tick gil change" race. Implementation lives in `AddonStateTracker`; the window is a slider on the settings window's Data tab.

Each classified event also writes a one-line breadcrumb at `IPluginLog.Information` level:
```
Diff Self() +150 -> NpcShopSell; recentAddons=[Shop]
```
Useful for `/xllog` post-mortem when a misclassification is reported. Prefer adding tests + tightening rules over expanding `AddonProbe.Watched`.

## IPC: `GilDelta.Hint` (提供端)

```
bool GilDelta.Hint(string category, string note, int ttlMs)
```

Another plugin tells GilDelta what the **next** matching gil change is, instead of
leaving it to guess from whichever addon happened to be open. Returns `true` when the
hint was accepted.

- `category` — a `GilEventCategory` **name**, case-insensitive. Numeric forms are
  rejected on purpose (`Enum.TryParse("3")` would otherwise succeed as `RetainerDeposit`).
  Unknown names return `false` and log one `Information` line **per distinct value**.
- `note` — free text. **By convention it starts with the calling plugin's own name**
  (`"Lifestream:利姆薩·羅敏薩"`): Dalamud does not tell a provider who called it, so
  the note is the only place the origin can be recorded. It lands verbatim in the
  event's `Note` as `rule=HintRule; hint=<note>`, which the timeline tab shows on hover.
- `ttlMs` — must be positive; clamped to `GilHintStore.MaxTtlMs` (5 min).

🔴 **The registered func runs on the *calling plugin's* thread.** `GilHintStore.Submit`
therefore touches only `ConcurrentQueue` / `ConcurrentDictionary`; the framework thread
picks hints up in `Plugin.OnFrameworkTick` → `GilHintStore.Drain()`, and `_active` is
framework-thread-only. Do not reach for anything else from `Submit`.

🔴 **`HintRule` consumption is destructive** — a matched hint is removed so it can never
label two changes. That is why `GameContext.Hints` is nullable and `GameContext.Empty`
leaves it null: `EventStore.Reclassify` replays historical events through the same rule
chain and must not be able to eat live hints.

`HintScope` gates which changes a hint may claim, on **both** the wallet kind and the
direction of the change. Without the direction half, a `Teleport` hint whose fee never
arrived would be eaten by the retainer payout that lands two seconds later.

Rule order is `PairedTransferRule` → `HintRule` → everything addon-driven: a
Self↔Retainer pair is self-evidently a transfer, but "an addon was open" is a much
weaker signal than "a plugin said what it just did".

## Known-deferred classification gaps

- **Teleport** was dropped in commit `69c3ce1` because the `Teleport` addon doesn't open for every trip (favourites, world-map right-click, direct aetheryte interactions) — and has been **brought back** as `TeleportRule`. The original objection was about false negatives, and those still just fall through to Misc, which is exactly where they were with no rule at all. False positives are the real risk, and the defence is position: the rule sits last, immediately before `MiscRule`, so every other rule gets first refusal. Trips that skip the addon are still Misc unless a plugin sends a `Teleport` hint.
- **Retainer withdrawal vs sale.** `RetainerSaleRule`'s Self-side branch fires on `RetainerList` (the bell menu), which covers withdrawals too, so a withdrawal whose retainer-side diff never arrived is recorded as a sale. `PairedTransferRule` catches it whenever both sides are visible, and a `RetainerWithdraw` hint overrides it outright. Left as-is deliberately: 'retainer money' is closer to the truth than Misc.
- **Retainer vendor sale.** A retainer selling to the NPC vendor pays the *player's* wallet while the retainer sell list is open (market proceeds land on the retainer's own wallet). `NpcShopSellRule` therefore takes a Self +Δ with `RetainerSellList`/`RetainerSell` in the window as `NpcShopSell` (`via=retainer` in the note) — but only when neither `RetainerList` nor `RetainerTaskResult` is also there, since the bell menu makes it indistinguishable from a withdrawal; that case stays `RetainerSale` as above. Rows recorded before this rule keep the category they were written with; nothing is rewritten.
- **FC chest decrease** has no rule. Only increases are classified (`FreeCompanyChestDeposit`); a withdrawal falls through to Misc.
- **MarketBoard sale** doesn't have a dedicated rule. Marketboard sales appear as a Retainer +Δ when the player retrieves them, so they're classified as `RetainerSale`. Splitting them out would require correlating with retainer market queue state — possible but deferred to v1.x.
- **Quest reward** and similar small Self +Δ events fall through to `Misc`. Hard to detect without addon hooks.

## Persistence

- One JSONL file per character at `%AppData%\XIVLauncher\pluginConfigs\GilDelta\events\<contentId>.jsonl`,
  managed by `EventStoreSet` (one `EventStore` per content id, created on demand).
- Schema: `{"v":1,"ts":"<iso>","wallet":{"kind":"...","id":"..."},"amount":<long>,"category":"...","note":"rule=<RuleName>; ...","cid":<ulong>}`.
  - 🔴 **`v` stays 1 even though `cid` was added later, on purpose.** `cid` is optional and
    `LoadAll` rejects any version it does not know — bumping the version would make every row
    this build writes silently invisible to an older build of the plugin.
  - `cid` is omitted entirely when the owner is unknown, so rows stay byte-identical to v1's.
  - On load, a row with no `cid` inherits the **file's** content id. The file name is a recorded
    owner, not a guess about who happens to be logged in; only `events/0.jsonl` stays unknown.
  - `suspect` is written only when the user has excluded that record, for the same reason
    `cid` is optional: an untouched log stays byte-identical to what the previous build wrote.
    An excluded record stays in the file and stays loadable; it just stops counting.
- Append-only on the live path. `EventStore.Reclassify(from, to, inferrer)` is the only mutation; it preserves the original as `<path>.jsonl.bak` before atomic-rename.
  🔴 Reclassify has to carry `ContentId` across by hand (`reclassified with { ContentId = ev.ContentId }`) — the rules build a fresh event and know nothing about owners.
- `EventLog` is the in-memory cache for the **whole account**, with per-character buckets
  (`For(contentId)`) maintained as events go in. Windows always read from there, never disk.
- `characters.json` — the roster (content id, name, home world), for labels in the picker.
- `wallets.json` — the last wallet reading per character, which is the only way an account-wide
  total can exist: the game only ever exposes the wallets of the character you are logged in as.
- Both of those go through `Storage/SerialJsonWriter`: serialize on the framework thread, write in
  the background, last-write-wins, one writer at a time, temp file + rename. No lock is ever held
  across file I/O.
- `daily/<contentId>.jsonl` — the settlement book, one line per local day, whole-file rewrite
  through `SerialJsonWriter`. Same threading discipline.

## Daily settlements

`Events/DailyLedger` keeps one `DailySummary` per (character, local day) and persists it under
`daily/`. It is **not** a cache of the event log; it is a second record with different lifetime
rules, and each of those exists for a reason:

- 🔴 **A day with no row means the plugin never observed that day.** That is a different fact from
  "the day's net came to zero", and it is the one the event list cannot express — which is exactly
  why `ChartTab` was rewritten off `PlotHistogram`. A wallet reading alone is enough to create the
  row, so a quiet session still registers as an observed day.
- 🔴 **A settled day outlives its events.** `LoadLedger` runs BEFORE `ApplyRetention` in
  `LoadPersistedState`, so the day retention is about to delete gets its totals written down first.
  `BackfillFromEvents` then never recomputes a day that already has a row.
- **The exception is today**, which is rebuilt from the log at every start (`ResetEvents` then
  re-add). The day is still running, all of its events are in memory, and the rebuild heals the case
  where the game went down between an event being appended and the settlement being written out.
  🔴 The decision is taken once per (character, day) and remembered — asking "do I already know this
  day?" per event flips to true the moment the first event creates the row, and every event after it
  would be dropped. There is a test for exactly that.
- 🔴 **`Retract` is what lets "exclude this record" reach a settled day.** The chart and the
  daily tab read this book, not the event list, so a record dropped only from the event list
  would leave a fabricated day still flattening the chart. A settled day cannot be recomputed
  from events (retention may have deleted them), so it is adjusted in place; `BackfillFromEvents`
  skips excluded records so the rebuild of *today* agrees with that adjustment.
- **The FC-chest half lives in its own buckets** (`fcByCategory` / `fcEvents`), which is what lets
  `Configuration.ExcludeFreeCompanyChest` be flipped either way and still give an exact answer for a
  day that is already on disk.
- **Opening / closing totals are the first and last wallet reading of the day**, and stay null when
  none was taken. Nullable all the way to the UI, which prints `?`. An aggregate (all-characters) row
  leaves them null on purpose: each character is read at a different moment, so summing them would
  produce a number that never existed.
- **The day an event belongs to comes from the event's own timestamp offset**
  (`DailyLedger.LocalDayOf`), not from the framework tick noticing midnight. The tick's date check
  only decides *when to flush*, so nothing breaks if it is late.
- Written out throttled (`LedgerPersistInterval`, 20s), plus unconditionally on date rollover, at
  logout, and in `Dispose`.

## Excluding the free company chest

`Configuration.ExcludeFreeCompanyChest` (default **false** — existing users' numbers do not move).
`Events/WalletExclusion` applies it in `Plugin.BuildWidgetContext` / `BuildDashboardContext`, once,
before the context is built, so every derived figure agrees.

- 🔴 **It keys on the wallet KIND, never on the category.** Chest money reaches the log as
  `FreeCompanyChestDeposit` (observed increase), `SubmarineReturn` (claimed over IPC) and `Misc`
  (a decrease, which has no rule at all). A category filter leaks two of the three.
- 🔴 **Nothing stops being recorded.** It is a display filter; turning it back off restores the whole
  history. Data that is not written cannot be recovered, which is why the setting was built this way
  round.
- Context for why anyone wants it: `WalletReader.TryReadFreeCompany` emits the chest with amount 0
  whenever the game is not reporting it (away from the chest), so **walking into the company house
  produces a 0 → N diff that `FreeCompanyChestDepositRule` records as a real deposit**. One such row
  in the test data was +50,000,000 — enough on its own to flatten every other day on the chart.
  That underlying baseline behaviour was left alone deliberately; the exclusion neutralises it for
  anyone who turns it on, and fixing the reader is a separate decision.

## Multi-character rules (v1.1)

- **`Plugin.TrackActiveCharacter()` on the framework tick is the authority**, not the `Login`
  event — `Login` fires before `PlayerState` is populated. It is subscribed BEFORE
  `WalletWatcher`'s tick, and Dalamud invokes a plugin's `Framework.Update` subscribers in
  subscription order (`PluginErrorHandler.InvokeAndCatch` runs the whole multicast delegate;
  `ProfileAndInvoke` walks `Delegate.EnumerateInvocationList`), so the active character is always
  up to date before wallets are diffed.
  🔴 That also means an exception escaping `OnFrameworkTick` would skip `WalletWatcher.Tick` for
  that frame — hence the per-step try/catch in there.
- **`WalletWatcher` takes an `isEnabled` gate** (`() => _activeContentId != 0`). While it is false
  the baseline is dropped, so the next character's first reading seeds a fresh baseline instead of
  being diffed against the previous character's gil. Without this, "log out of A, log in as B" is
  recorded as one enormous gil swing.
- **`WalletId(Self, "")` is identical for every character.** Never key an account-wide merge by
  `WalletId` alone — it would collapse the whole account into one row. `WalletSnapshotStore.Merge`
  de-duplicates only the FC chest (keyed by FC name, newest reading wins), which is the one wallet
  two characters genuinely share.
- **Nothing is recorded while no character is loaded** (`HandleDiff` returns early on owner 0);
  inventing an owner would be worse than dropping the record.

## Per-frame cost of the views

- The widget, the dashboard and the detail window ask for their context every frame, but
  `Plugin.WidgetView` / `DashboardView` rebuild it only when `ViewCache` sees an input move:
  `EventLog.Version`, `WalletSnapshotStore.Version`, `_ledgerVersion` (dashboard only), the active
  character, the scope, the chest exclusion, the label / roster cache instances, the local minute,
  and the watcher's wallet reading (compared by contents - it is a new list every tick).
- 🔴 A new input read by `BuildWidgetContext` / `BuildDashboardContext` has to join `ViewInputs`, and a
  new way of changing `EventLog` or `WalletSnapshotStore` has to bump its `Version`. Miss either and
  the view keeps showing the old figure, with no error, until the minute rolls over.
- Contexts are reused across frames: `WidgetContext.TotalHovered` is cleared by the window before each
  draw, `Now` can be up to a minute old (the Trend sparkline reads the clock itself), and the timeline
  tab keeps its sorted list per context instance.
- A cache hit allocates nothing; `ViewCacheTests.A_hit_does_not_allocate` guards that.

## Build

```
dotnet build -c Release
```

- Output: `bin/Release/GilDelta/GilDelta.dll` (loadable) + `bin/Release/GilDelta/latest.zip` (distribution).
- The `NETSDK1057` preview-SDK info note is harmless.

```
dotnet test
```

xUnit project at `tests/GilDelta.Tests/`. Pure-logic only — no Dalamud references. 🔴 That is a hard runtime constraint, not a style rule: a test that touches any Dalamud type dies with `FileNotFoundException: Could not load file or assembly 'Dalamud, Version=13.0.0.16'`. `Configuration` counts, because it implements `IPluginConfiguration` - so a helper that needs testing takes its own plain struct instead of the config object (see `WidgetHideSettings`). ImGui rendering is verified in-game, not by tests. Current count: 299 tests covering Wallet types, Inferrer + every rule, RuleChain integration, EventStore (append/load/reclassify), EventLog, WidgetContext, BalanceRows, WidgetVisibility, DashboardContext, DailySummary, DailyLedger, WalletExclusion, ChartScale.

## Distribution

- Index repo: [`yagi2/dalamud-plugins`](https://github.com/yagi2/dalamud-plugins). End users add the index URL to Dalamud once and get every yagi2 plugin in their installer. New release = bump `AssemblyVersion`/`Changelog` in that repo's `repo.json`.
- Release artifacts: GHA workflow at `.github/workflows/release.yml` triggers on `release: published`, builds on `windows-latest`, downloads Dalamud reference assemblies from `https://goatcorp.github.io/dalamud-distrib/latest.zip` (stable, NOT `stg/`), uploads `bin/Release/GilDelta/latest.zip` via `gh release upload --clobber`.

## Reference plugins

- `..\RepeatBuy` — yagi2's first Dalamud plugin. Same Dalamud SDK 15. Uses an older addon-driven flow (single command) without any DI surface; useful for csproj/manifest reference only.
- `..\Restocker` — yagi2's second plugin. Larger codebase, similar shape to GilDelta. Particularly useful for ImGui binding patterns and the icon convention.

## What not to do

- Don't write to game state. WalletReader is read-only; stay that way.
- Don't add a settings checkbox without a reading end. `HideInCutscene` and `HideInDuty` shipped for several versions as write-only config: the box ticked, the value persisted, and nothing on the other end ever looked at it - no error, no log line, just a setting that did nothing. Grep the field name before assuming a setting works.
- Don't expand `AddonProbe.Watched` speculatively. Add an addon name only when a `Diff ... -> Misc; recentAddons=[]` log line shows the addon was missing during a real misclassification. (That bar has been met once: a TC user's 16 consecutive events were all `Misc` with `addons=[]`, which is what the teleport / grand company / retainer / FC chest entries were added on.)
- Don't invent an addon name. Every entry in `Watched` was checked against an `[Addon("...")]` attribute in FFXIVClientStructs or a live `GetAddonByName` call in another fleet plugin — a wrong name never matches and never complains. `TelepotTown` is genuinely misspelled in the game.
- Don't bypass the recency window — it exists to absorb the WalletWatcher / addon-close timing race.
- Don't commit anything under `docs/superpowers/` or `.superpowers/` — those are for local agent process artifacts and are intentionally `.gitignore`d.
- Don't skip `<PlatformTarget>x64</PlatformTarget>` and the AppendTargetFrameworkToOutputPath / AppendRuntimeIdentifierToOutputPath / OutputPath trio. Without them SDK 15 routes the build to `bin/x64/Release/` and Dev Plugin Locations break silently.
- Don't ship a PNG icon without sRGB + gAMA + pHYs chunks. The plugin installer will leave the icon slot blank.
