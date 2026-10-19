namespace GilDelta.Localization;

public static class Strings
{
    // Set/read only from the framework thread. Dalamud guarantees serial access there.
    private static Language _current = Language.English;

    public static void SetLanguage(Language lang) => _current = lang;

    /// <summary>The language currently in effect. Views that cache translated text key their cache on it.</summary>
    public static Language Current => _current;

    private static string T(string en, string ja, string de, string fr, string zh, string ko, string tw) =>
        _current switch
        {
            Language.English            => en,
            Language.Japanese           => ja,
            Language.German             => de,
            Language.French             => fr,
            Language.SimplifiedChinese  => zh,
            Language.Korean             => ko,
            Language.ChineseTraditional => tw,
            _                           => en,
        };

    /// <summary>
    /// A string that only has a Traditional Chinese translation: every other language
    /// gets the English. Strings that really are translated still use <c>T</c>.
    /// </summary>
    private static string TTw(string en, string tw) =>
        _current == Language.ChineseTraditional ? tw : en;

    // ─── Generic ───
    public static string PluginName => "GilDelta";

    // ─── Widget ───
    public static string TotalWealth => T(
        "Total wealth",
        "総資産",
        "Gesamtvermögen",
        "Patrimoine total",
        "总资产",
        "총자산",
        "總資產"
    );

    private static Language? _totalWealthUpperFor;
    private static string _totalWealthUpper = string.Empty;

    /// <summary>
    /// <see cref="TotalWealth"/> in capitals, as the widget and chart headings print it.
    /// Cached per language: <c>ToUpperInvariant</c> allocates, and the heading is drawn every frame.
    /// </summary>
    public static string TotalWealthUpper
    {
        get
        {
            if (_totalWealthUpperFor != _current)
            {
                _totalWealthUpper = TotalWealth.ToUpperInvariant();
                _totalWealthUpperFor = _current;
            }
            return _totalWealthUpper;
        }
    }

    public static string Today => T("today", "今日", "heute", "aujourd'hui", "今天", "오늘", "今天");

    public static string LastUpdate => T(
        "last update",
        "最終更新",
        "letzte Aktualisierung",
        "dernière maj",
        "最近更新",
        "최근 업데이트",
        "最近更新"
    );

    public static string TileWeek  => T("week",  "week",  "week",  "week",  "week",  "week",  "本週");
    public static string TileMonth => T("month", "month", "month", "month", "month", "month", "本月");

    // ─── Dashboard tabs ───
    public static string TabTimeline  => T("Timeline", "タイムライン", "Zeitachse", "Chronologie", "时间线", "타임라인", "時間軸");
    public static string TabChart     => T("Chart",    "チャート",     "Diagramm",  "Graphique",   "图表",    "차트", "圖表");
    public static string TabBreakdown => T("Breakdown","内訳",         "Aufschlüsselung","Répartition","明细", "내역", "明細");
    public static string TabHeatmap   => T("Heatmap",  "ヒートマップ", "Heatmap",   "Carte de chaleur","热力图","히트맵", "熱力圖");
    public static string TabDaily     => T("Daily",    "日次集計",     "Tagesbilanz","Bilan quotidien","每日结算","일일 정산", "每日結算");

    // ─── Categories ───
    public static string CategorySubmarineReturn => T(
        "Submarine return", "潜水艦帰還", "Rückkehr U-Boot", "Retour sous-marin", "潜艇返回", "잠수함 귀환", "潛水艇返回"
    );
    public static string CategoryFreeCompanyChestDeposit => T(
        "FC chest deposit", "FC金庫入金", "FG-Truhen-Einzahlung", "Dépôt coffre CL", "部队储物柜存入", "부대 금고 입금", "公會儲物櫃存入"
    );
    public static string CategoryRetainerSale => T(
        "Retainer sale", "リテイナー売上", "Gehilfenverkauf", "Vente de servant", "雇员销售", "고용인 판매", "僱員銷售"
    );
    public static string CategoryRetainerWithdraw => T(
        "Retainer withdraw", "リテイナー引出", "Gehilfen-Abhebung", "Retrait servant", "雇员取款", "고용인 인출", "僱員提款"
    );
    public static string CategoryRetainerDeposit => T(
        "Retainer deposit", "リテイナー預入", "Gehilfen-Einzahlung", "Dépôt servant", "雇员存款", "고용인 입금", "僱員存款"
    );
    public static string CategoryNpcShopBuy => T(
        "NPC purchase", "NPC購入", "NSC-Kauf", "Achat NPC", "NPC购买", "NPC 구매", "NPC購買"
    );
    public static string CategoryNpcShopSell => T(
        "NPC sale", "NPC売却", "NSC-Verkauf", "Vente NPC", "NPC出售", "NPC 판매", "NPC出售"
    );
    public static string CategoryMarketBoardBuy => T(
        "Marketboard purchase", "マケボ購入", "Marktbrett-Kauf", "Achat marché", "市场购买", "마켓보드 구매", "市場購買"
    );
    public static string CategoryRepair => T(
        "Repair", "修理", "Reparatur", "Réparation", "修理", "수리", "修理"
    );
    public static string CategoryTeleport => T(
        "Teleport", "テレポ", "Teleport", "Téléportation", "传送", "텔레포트", "傳送"
    );
    public static string CategoryMailReceive => T(
        "Mail received", "モグレター受取", "Brief erhalten", "Courrier reçu", "邮件收款", "우편 수령", "郵件收款"
    );
    public static string CategoryMailSend => T(
        "Mail sent", "モグレター送付", "Brief gesendet", "Courrier envoyé", "邮件寄出", "우편 발송", "郵件寄出"
    );
    public static string CategoryMisc => T(
        "Other", "その他", "Sonstiges", "Autre", "其他", "기타", "其他"
    );

    public static string CategoryCharacterTransfer => T(
        "Character transfer", "キャラ間送金", "Charaktertransfer",
        "Transfert entre personnages", "角色间转账", "캐릭터 간 이체", "角色間轉帳"
    );

    // ─── Settings window ───
    public static string DetectCharacterTransfers => TTw(
        "Pair transfers between your own characters", "辨識角色間轉帳");

    public static string DetectCharacterTransfersHelp => TTw(
        "When another character on this account has an equal, opposite and equally unexplained gil change inside the window, both records are labelled as a transfer. They stay in the timeline but stop counting towards the daily totals, because the same gil is sitting on both characters.",
        "同一帳號的另一個角色在時間窗內有金額相同、方向相反、而且同樣查不出來源的變動時，兩筆都會標成「角色間轉帳」。標記後仍留在時間軸上，但不再計入每日結算——那筆錢只是換了一個角色拿著。");

    public static string CharacterTransferWindowLabel => TTw(
        "Transfer window (minutes)", "轉帳配對時間窗（分鐘）");

    public static string CharacterTransferWindowHelp => TTw(
        "How far apart the two halves may sit. Too short misses a transfer that is collected later; too long can pair two unrelated changes that happen to be the same size.",
        "兩筆的間隔上限。太短會漏掉隔一陣子才去領的那一半；太長則可能把兩筆金額剛好相同、其實無關的變動配在一起。");

    public static string SettingsWindowTitle  => T("Settings",  "Settings",  "Settings",  "Settings",  "Settings",  "Settings",  "設定");
    public static string SettingsTabGeneral   => T("General",   "General",   "General",   "General",   "General",   "General",   "一般");
    public static string SettingsTabWidget    => T("Widget",    "Widget",    "Widget",    "Widget",    "Widget",    "Widget",    "小工具");
    public static string SettingsTabDashboard => T("Dashboard", "Dashboard", "Dashboard", "Dashboard", "Dashboard", "Dashboard", "儀表板");
    public static string SettingsTabData      => T("Data",      "Data",      "Data",      "Data",      "Data",      "Data",      "資料");

    public static string LanguageAuto => T(
        "Auto (follow client)", "Auto (follow client)", "Auto (follow client)",
        "Auto (follow client)", "Auto (follow client)", "Auto (follow client)",
        "自動（跟隨遊戲語言）"
    );
    public static string LanguageLabel => T("Language", "Language", "Language", "Language", "Language", "Language", "語言");
    public static string ThemeLabel    => T("Theme",    "Theme",    "Theme",    "Theme",    "Theme",    "Theme",    "主題");

    public static string ShowWidget => T(
        "Show widget", "Show widget", "Show widget", "Show widget", "Show widget", "Show widget", "顯示小工具"
    );
    public static string DensityLabel   => T("Density", "Density", "Density", "Density", "Density", "Density", "密度");
    public static string DensityMinimal => T("Minimal", "Minimal", "Minimal", "Minimal", "Minimal", "Minimal", "簡約");
    public static string DensityCompact => T("Compact", "Compact", "Compact", "Compact", "Compact", "Compact", "精簡");
    public static string DensityTrend   => T("Trend",   "Trend",   "Trend",   "Trend",   "Trend",   "Trend",   "趨勢");
    public static string DensityTile    => T("Tile",    "Tile",    "Tile",    "Tile",    "Tile",    "Tile",    "方塊");

    public static string HideInCutscene => T(
        "Hide in cutscene", "Hide in cutscene", "Hide in cutscene",
        "Hide in cutscene", "Hide in cutscene", "Hide in cutscene", "過場動畫時隱藏"
    );
    public static string HideInDuty => T(
        "Hide in duty", "Hide in duty", "Hide in duty",
        "Hide in duty", "Hide in duty", "Hide in duty", "副本中隱藏"
    );
    public static string LockWidgetPosition => T(
        "Lock widget position", "Lock widget position", "Lock widget position",
        "Lock widget position", "Lock widget position", "Lock widget position", "鎖定小工具位置"
    );
    public static string ResetWidgetPosition => T(
        "Reset widget position", "Reset widget position", "Reset widget position",
        "Reset widget position", "Reset widget position", "Reset widget position", "重設小工具位置"
    );

    public static string DefaultTabLabel => T(
        "Default tab", "Default tab", "Default tab", "Default tab", "Default tab", "Default tab", "預設分頁"
    );

    public static string RetentionDaysLabel => T(
        "Retention (days, 0 = forever)", "Retention (days, 0 = forever)", "Retention (days, 0 = forever)",
        "Retention (days, 0 = forever)", "Retention (days, 0 = forever)", "Retention (days, 0 = forever)",
        "保留天數（0＝永久保留）"
    );
    public static string AddonRecencyLabel => T(
        "Window recency (seconds)", "Window recency (seconds)", "Window recency (seconds)",
        "Window recency (seconds)", "Window recency (seconds)", "Window recency (seconds)",
        "視窗判定時間（秒）"
    );
    // ─── Characters ───
    public static string CharacterLabel => T(
        "Character", "Character", "Character", "Character", "Character", "Character", "角色"
    );
    public static string CharacterAll => T(
        "All characters", "All characters", "All characters",
        "All characters", "All characters", "All characters", "全部角色"
    );
    public static string CharacterFollowCurrent => T(
        "Follow current character", "Follow current character", "Follow current character",
        "Follow current character", "Follow current character", "Follow current character",
        "跟隨目前角色"
    );
    public static string CharacterUnknown => T(
        "Unknown character", "Unknown character", "Unknown character",
        "Unknown character", "Unknown character", "Unknown character", "未知角色"
    );
    public static string CharacterUnknownHint => T(
        "Records written before multi-character support; the owning character is unknown.",
        "Records written before multi-character support; the owning character is unknown.",
        "Records written before multi-character support; the owning character is unknown.",
        "Records written before multi-character support; the owning character is unknown.",
        "Records written before multi-character support; the owning character is unknown.",
        "Records written before multi-character support; the owning character is unknown.",
        "早於多角色支援的紀錄，無法判定屬於哪個角色。"
    );
    public static string AccountTotalHint => T(
        "The all-characters total adds up the last wallet reading taken for each character. "
        + "Characters you are not logged in as keep the numbers from the last time you played them.",
        "The all-characters total adds up the last wallet reading taken for each character. "
        + "Characters you are not logged in as keep the numbers from the last time you played them.",
        "The all-characters total adds up the last wallet reading taken for each character. "
        + "Characters you are not logged in as keep the numbers from the last time you played them.",
        "The all-characters total adds up the last wallet reading taken for each character. "
        + "Characters you are not logged in as keep the numbers from the last time you played them.",
        "The all-characters total adds up the last wallet reading taken for each character. "
        + "Characters you are not logged in as keep the numbers from the last time you played them.",
        "The all-characters total adds up the last wallet reading taken for each character. "
        + "Characters you are not logged in as keep the numbers from the last time you played them.",
        "「全部角色」的總資產是把每個角色最後一次讀到的錢包相加。"
        + "沒有登入的角色，沿用上次遊玩時讀到的數字。"
    );
    public static string OldestReadingAt => T(
        "oldest reading", "oldest reading", "oldest reading",
        "oldest reading", "oldest reading", "oldest reading", "最舊資料時間"
    );
    public static string NoWalletReadingYet => T(
        "No wallet reading for this character yet - log in as them once.",
        "No wallet reading for this character yet - log in as them once.",
        "No wallet reading for this character yet - log in as them once.",
        "No wallet reading for this character yet - log in as them once.",
        "No wallet reading for this character yet - log in as them once.",
        "No wallet reading for this character yet - log in as them once.",
        "尚未讀到這個角色的錢包資料，登入該角色一次即可補上。"
    );
    public static string WidgetAllCharacters => T(
        "Widget shows whole-account total", "Widget shows whole-account total",
        "Widget shows whole-account total", "Widget shows whole-account total",
        "Widget shows whole-account total", "Widget shows whole-account total",
        "小工具顯示整個帳號合計"
    );

    public static string RetentionDaysHelp => T(
        "Records older than the window are deleted permanently and cannot be recovered. 0 keeps everything forever.",
        "Records older than the window are deleted permanently and cannot be recovered. 0 keeps everything forever.",
        "Records older than the window are deleted permanently and cannot be recovered. 0 keeps everything forever.",
        "Records older than the window are deleted permanently and cannot be recovered. 0 keeps everything forever.",
        "Records older than the window are deleted permanently and cannot be recovered. 0 keeps everything forever.",
        "Records older than the window are deleted permanently and cannot be recovered. 0 keeps everything forever.",
        "會永久刪除舊紀錄，刪掉就救不回來；0 表示永久保留。"
    );
    public static string AddonRecencyHelp => T(
        "How long a window counts as \"was just open\" when working out what a gil change was. Longer helps when other plugins open and close windows faster than they can be seen; too long starts crediting the wrong window.",
        "How long a window counts as \"was just open\" when working out what a gil change was. Longer helps when other plugins open and close windows faster than they can be seen; too long starts crediting the wrong window.",
        "How long a window counts as \"was just open\" when working out what a gil change was. Longer helps when other plugins open and close windows faster than they can be seen; too long starts crediting the wrong window.",
        "How long a window counts as \"was just open\" when working out what a gil change was. Longer helps when other plugins open and close windows faster than they can be seen; too long starts crediting the wrong window.",
        "How long a window counts as \"was just open\" when working out what a gil change was. Longer helps when other plugins open and close windows faster than they can be seen; too long starts crediting the wrong window.",
        "How long a window counts as \"was just open\" when working out what a gil change was. Longer helps when other plugins open and close windows faster than they can be seen; too long starts crediting the wrong window.",
        "判斷金幣變動是什麼時，「剛才開過這個視窗」要往回算多久。調長對「其他插件開關視窗比看得到的還快」有幫助，調太長則會開始算到不相干的視窗上。"
    );
    public static string RetentionDaysWarning => T(
        "Deletion is on: records past the window go at startup and once a day.",
        "Deletion is on: records past the window go at startup and once a day.",
        "Deletion is on: records past the window go at startup and once a day.",
        "Deletion is on: records past the window go at startup and once a day.",
        "Deletion is on: records past the window go at startup and once a day.",
        "Deletion is on: records past the window go at startup and once a day.",
        "刪除已啟用：超過保留天數的紀錄會在啟動時與每天各清除一次。"
    );

    public static string ComingInV11 => T(
        "Reclassify range / Export CSV: coming in v1.1", "Reclassify range / Export CSV: coming in v1.1",
        "Reclassify range / Export CSV: coming in v1.1", "Reclassify range / Export CSV: coming in v1.1",
        "Reclassify range / Export CSV: coming in v1.1", "Reclassify range / Export CSV: coming in v1.1",
        "重新分類區間／匯出 CSV：將於 v1.1 推出"
    );

    // ─── Breakdown tab ───
    public static string PeriodToday   => T("Today",   "Today",   "Today",   "Today",   "Today",   "Today",   "今天");
    public static string Period7Days   => T("7 days",  "7 days",  "7 days",  "7 days",  "7 days",  "7 days",  "7天");
    public static string Period30Days  => T("30 days", "30 days", "30 days", "30 days", "30 days", "30 days", "30天");
    public static string PeriodAll     => T("All",     "All",     "All",     "All",     "All",     "All",     "全部");
    public static string NoEventsInPeriod => T(
        "(no events in this period)", "(no events in this period)", "(no events in this period)",
        "(no events in this period)", "(no events in this period)", "(no events in this period)",
        "（此區間內尚無事件）"
    );

    // ─── Chart tab ───
    // The chart window is not a fixed 30 days, so the label names no length.
    public static string ChartWealthOverlay => T(
        "wealth", "wealth", "wealth", "wealth", "wealth", "wealth", "資產走勢"
    );
    public static string ChartDailyNetOverlay => T(
        "daily net", "daily net", "daily net", "daily net", "daily net", "daily net", "每日淨額"
    );
    public static string ChartRangeLabel => T(
        "Range", "Range", "Range", "Range", "Range", "Range", "區間"
    );
    public static string ChartRangeAuto => T(
        "Auto", "Auto", "Auto", "Auto", "Auto", "Auto", "自動"
    );
    public static string Period90Days => T("90 days", "90 days", "90 days", "90 days", "90 days", "90 days", "90天");
    public static string ChartLegend => T(
        "hatched = no records that day; flat line = recorded, net came to 0",
        "hatched = no records that day; flat line = recorded, net came to 0",
        "hatched = no records that day; flat line = recorded, net came to 0",
        "hatched = no records that day; flat line = recorded, net came to 0",
        "hatched = no records that day; flat line = recorded, net came to 0",
        "hatched = no records that day; flat line = recorded, net came to 0",
        "斜紋＝當天完全沒有紀錄；平線＝有紀錄但淨額剛好是 0"
    );
    public static string ChartNoDataDay => T(
        "no records", "no records", "no records", "no records", "no records", "no records",
        "沒有紀錄"
    );
    public static string ChartScaleCap => T(
        "scale capped at", "scale capped at", "scale capped at",
        "scale capped at", "scale capped at", "scale capped at", "刻度上限"
    );
    public static string ChartFullScale => T(
        "Full scale", "Full scale", "Full scale", "Full scale", "Full scale", "Full scale",
        "完整刻度"
    );
    public static string ChartFullScaleHelp => T(
        "Off: one outlying day is drawn clipped, with a marker, so every other day stays readable. On: the tallest day sets the scale and flattens the rest.",
        "Off: one outlying day is drawn clipped, with a marker, so every other day stays readable. On: the tallest day sets the scale and flattens the rest.",
        "Off: one outlying day is drawn clipped, with a marker, so every other day stays readable. On: the tallest day sets the scale and flattens the rest.",
        "Off: one outlying day is drawn clipped, with a marker, so every other day stays readable. On: the tallest day sets the scale and flattens the rest.",
        "Off: one outlying day is drawn clipped, with a marker, so every other day stays readable. On: the tallest day sets the scale and flattens the rest.",
        "Off: one outlying day is drawn clipped, with a marker, so every other day stays readable. On: the tallest day sets the scale and flattens the rest.",
        "關閉時，特別大的那一天會被截斷並標上記號，其餘各天才看得清楚；開啟時由最高的那天決定刻度，其餘會被壓平。"
    );
    public static string ChartClipped => T(
        "clipped", "clipped", "clipped", "clipped", "clipped", "clipped", "已截斷"
    );
    public static string ChartWealthUnknownBefore => T(
        "Wealth before this point cannot be worked back: there are days with no records in between.",
        "Wealth before this point cannot be worked back: there are days with no records in between.",
        "Wealth before this point cannot be worked back: there are days with no records in between.",
        "Wealth before this point cannot be worked back: there are days with no records in between.",
        "Wealth before this point cannot be worked back: there are days with no records in between.",
        "Wealth before this point cannot be worked back: there are days with no records in between.",
        "再往前的資產無法回推：中間有沒有紀錄的日子。"
    );

    // ─── Daily settlement tab ───
    public static string DailyColumnDate   => T("date",   "date",   "date",   "date",   "date",   "date",   "日期");
    public static string DailyColumnEvents => T("events", "events", "events", "events", "events", "events", "事件數");
    public static string DailyColumnNet    => T("net",    "net",    "net",    "net",    "net",    "net",    "淨額");
    public static string DailyColumnOpen   => T("opening","opening","opening","opening","opening","opening","期初");
    public static string DailyColumnClose  => T("closing","closing","closing","closing","closing","closing","期末");
    public static string DailyNoRecords => T(
        "(no daily settlements yet)", "(no daily settlements yet)", "(no daily settlements yet)",
        "(no daily settlements yet)", "(no daily settlements yet)", "(no daily settlements yet)",
        "（尚無每日結算紀錄）"
    );
    public static string DailyUnknownTotalHint => T(
        "No wallet reading was taken that day, so the opening / closing totals are unknown.",
        "No wallet reading was taken that day, so the opening / closing totals are unknown.",
        "No wallet reading was taken that day, so the opening / closing totals are unknown.",
        "No wallet reading was taken that day, so the opening / closing totals are unknown.",
        "No wallet reading was taken that day, so the opening / closing totals are unknown.",
        "No wallet reading was taken that day, so the opening / closing totals are unknown.",
        "那一天沒有讀到錢包資料，所以期初／期末是未知的。"
    );
    public static string DailyAggregateHint => T(
        "In the all-characters view only nets and categories are added up. Each character's wallet is read at a different moment, so opening / closing totals are not summed across the account and stay unknown.",
        "In the all-characters view only nets and categories are added up. Each character's wallet is read at a different moment, so opening / closing totals are not summed across the account and stay unknown.",
        "In the all-characters view only nets and categories are added up. Each character's wallet is read at a different moment, so opening / closing totals are not summed across the account and stay unknown.",
        "In the all-characters view only nets and categories are added up. Each character's wallet is read at a different moment, so opening / closing totals are not summed across the account and stay unknown.",
        "In the all-characters view only nets and categories are added up. Each character's wallet is read at a different moment, so opening / closing totals are not summed across the account and stay unknown.",
        "In the all-characters view only nets and categories are added up. Each character's wallet is read at a different moment, so opening / closing totals are not summed across the account and stay unknown.",
        "「全部角色」只合計淨額與分類。每個角色的錢包是在不同時間讀到的，所以期初／期末不跨角色相加，維持未知。"
    );
    public static string DailySettlementKept => T(
        "Daily settlements are kept even after the events behind them are deleted by the retention setting.",
        "Daily settlements are kept even after the events behind them are deleted by the retention setting.",
        "Daily settlements are kept even after the events behind them are deleted by the retention setting.",
        "Daily settlements are kept even after the events behind them are deleted by the retention setting.",
        "Daily settlements are kept even after the events behind them are deleted by the retention setting.",
        "Daily settlements are kept even after the events behind them are deleted by the retention setting.",
        "每日結算會一直保留：就算保留天數把背後的明細清掉了，這裡的每日數字仍在。"
    );
    public static string DailyTotalRow => T(
        "total shown", "total shown", "total shown", "total shown", "total shown", "total shown",
        "顯示區間合計"
    );

    // ─── Free company chest exclusion ───
    public static string ExcludeFcChest => T(
        "Leave the free company chest out of the totals",
        "Leave the free company chest out of the totals",
        "Leave the free company chest out of the totals",
        "Leave the free company chest out of the totals",
        "Leave the free company chest out of the totals",
        "Leave the free company chest out of the totals",
        "統計排除公會儲物櫃"
    );
    public static string ExcludeFcChestHelp => T(
        "Chest money is shared company money, and the game only reports it while you are at the chest - it reads as 0 everywhere else, so a single visit to the company house turns up as one huge deposit. Turning this on drops the chest out of every total, chart and breakdown. It is still RECORDED either way, so turning it back off brings the history straight back.",
        "Chest money is shared company money, and the game only reports it while you are at the chest - it reads as 0 everywhere else, so a single visit to the company house turns up as one huge deposit. Turning this on drops the chest out of every total, chart and breakdown. It is still RECORDED either way, so turning it back off brings the history straight back.",
        "Chest money is shared company money, and the game only reports it while you are at the chest - it reads as 0 everywhere else, so a single visit to the company house turns up as one huge deposit. Turning this on drops the chest out of every total, chart and breakdown. It is still RECORDED either way, so turning it back off brings the history straight back.",
        "Chest money is shared company money, and the game only reports it while you are at the chest - it reads as 0 everywhere else, so a single visit to the company house turns up as one huge deposit. Turning this on drops the chest out of every total, chart and breakdown. It is still RECORDED either way, so turning it back off brings the history straight back.",
        "Chest money is shared company money, and the game only reports it while you are at the chest - it reads as 0 everywhere else, so a single visit to the company house turns up as one huge deposit. Turning this on drops the chest out of every total, chart and breakdown. It is still RECORDED either way, so turning it back off brings the history straight back.",
        "Chest money is shared company money, and the game only reports it while you are at the chest - it reads as 0 everywhere else, so a single visit to the company house turns up as one huge deposit. Turning this on drops the chest out of every total, chart and breakdown. It is still RECORDED either way, so turning it back off brings the history straight back.",
        "儲物櫃裡是公會共有的錢，而且遊戲只有在你人在儲物櫃旁邊時才會回報金額，其他時候一律讀成 0；於是進一次公會房屋就會被記成一筆巨額存入。開啟後，總資產、圖表與明細都不再算入公會儲物櫃。無論開關，紀錄照樣會留下，關掉就會全部回來。"
    );
    public static string ExcludingFcChestNow => T(
        "(free company chest excluded)", "(free company chest excluded)", "(free company chest excluded)",
        "(free company chest excluded)", "(free company chest excluded)", "(free company chest excluded)",
        "（已排除公會儲物櫃）"
    );
    public static string FcChestUnreadableNow => T(
        "(free company chest unreadable)", "(free company chest unreadable)", "(free company chest unreadable)",
        "(free company chest unreadable)", "(free company chest unreadable)", "(free company chest unreadable)",
        "（公會儲物櫃讀不到）"
    );
    public static string FcChestUnreadableHelp => TTw(
        "The game only sends the free company chest balance while you are in the company house or have just opened the chest. The rest of the time it cannot be read, so it shows as \"?\" instead of 0 and is left out of the total — the total is therefore a lower bound. No free company chest income or spending is recorded while it cannot be read. One side effect of that caution: a chest that really does hold 0 also shows \"?\" until the server has sent a non-zero balance for that company at least once since you logged in, because a lone 0 cannot be told apart from \"not sent yet\". Nothing is broken - once the chest has been seen holding anything, a later 0 is shown as the real 0 it is.",
        "遊戲只有在你人在公會房屋、或剛開過儲物櫃時，才會把公會儲物櫃的餘額送過來；其他時候一律讀不到。讀不到的時候這裡顯示「?」而不是 0，總資產也不會算入它，所以那個總額是下限。讀不到的期間不會產生任何公會儲物櫃的收支紀錄——過去把讀不到當成 0，才會讓進一次公會房屋被記成一筆巨額存入。這樣謹慎有一個副作用：真的空的公會儲物櫃，在本次登入還沒收到過一次非零餘額之前，也會顯示「?」而不是 0——因為單獨一個 0 沒辦法和「還沒送過來」區分。這不是壞掉；只要伺服器送過一次有金額的餘額，之後的 0 就會照實顯示成 0。"
    );

    // ─── Dashboard / Widget shared ───
    public static string WaitingForGameState => T(
        "waiting for game state...", "waiting for game state...", "waiting for game state...",
        "waiting for game state...", "waiting for game state...", "waiting for game state...",
        "正在等待遊戲資料…"
    );

    // ─── Timeline tab ───
    public static string NoEventsRecordedYet => T(
        "(no events recorded yet)", "(no events recorded yet)", "(no events recorded yet)",
        "(no events recorded yet)", "(no events recorded yet)", "(no events recorded yet)",
        "（尚無記錄的事件）"
    );
    public static string ColumnWhen     => T("when",     "when",     "when",     "when",     "when",     "when",     "時間");
    public static string ColumnCategory => T("category", "category", "category", "category", "category", "category", "分類");
    public static string ColumnWallet   => T("wallet",   "wallet",   "wallet",   "wallet",   "wallet",   "wallet",   "錢包");
    public static string ColumnAmount   => T("amount",   "amount",   "amount",   "amount",   "amount",   "amount",   "金額");

    public static string WalletSelf    => T("self",      "self",      "self",      "self",      "self",      "self",      "自己");
    public static string WalletFcChest => T("fc-chest",  "fc-chest",  "fc-chest",  "fc-chest",  "fc-chest",  "fc-chest",  "公會儲物櫃");
    public static string ColumnActions  => T("", "", "", "", "", "", "");

    public static string TimelineExclude => T(
        "exclude", "exclude", "exclude", "exclude", "exclude", "exclude", "排除"
    );
    public static string TimelineRestore => T(
        "restore", "restore", "restore", "restore", "restore", "restore", "復原"
    );
    public static string TimelineExcludeHelp => T(
        "Stops this record counting towards any total, chart, breakdown or daily settlement. It is NOT deleted: it stays in the log with a flag on it, this row keeps showing it greyed out, and pressing restore puts it back exactly as it was. Meant for records that were never real - the fabricated free company chest deposits an earlier version produced, for instance.",
        "Stops this record counting towards any total, chart, breakdown or daily settlement. It is NOT deleted: it stays in the log with a flag on it, this row keeps showing it greyed out, and pressing restore puts it back exactly as it was. Meant for records that were never real - the fabricated free company chest deposits an earlier version produced, for instance.",
        "Stops this record counting towards any total, chart, breakdown or daily settlement. It is NOT deleted: it stays in the log with a flag on it, this row keeps showing it greyed out, and pressing restore puts it back exactly as it was. Meant for records that were never real - the fabricated free company chest deposits an earlier version produced, for instance.",
        "Stops this record counting towards any total, chart, breakdown or daily settlement. It is NOT deleted: it stays in the log with a flag on it, this row keeps showing it greyed out, and pressing restore puts it back exactly as it was. Meant for records that were never real - the fabricated free company chest deposits an earlier version produced, for instance.",
        "Stops this record counting towards any total, chart, breakdown or daily settlement. It is NOT deleted: it stays in the log with a flag on it, this row keeps showing it greyed out, and pressing restore puts it back exactly as it was. Meant for records that were never real - the fabricated free company chest deposits an earlier version produced, for instance.",
        "Stops this record counting towards any total, chart, breakdown or daily settlement. It is NOT deleted: it stays in the log with a flag on it, this row keeps showing it greyed out, and pressing restore puts it back exactly as it was. Meant for records that were never real - the fabricated free company chest deposits an earlier version produced, for instance.",
        "把這筆紀錄從所有總額、圖表、明細與每日結算裡拿掉。它不會被刪除：紀錄照樣留著，只是多了一個標記，這一列會以灰字繼續顯示，按「復原」就會原封不動回來。用途是處理根本沒發生過的紀錄——例如舊版本因為讀不到公會儲物櫃而記下的那種巨額存入。"
    );
    public static string TimelineRestoreHelp => T(
        "Puts this record back into every total, chart and daily settlement it was taken out of.",
        "Puts this record back into every total, chart and daily settlement it was taken out of.",
        "Puts this record back into every total, chart and daily settlement it was taken out of.",
        "Puts this record back into every total, chart and daily settlement it was taken out of.",
        "Puts this record back into every total, chart and daily settlement it was taken out of.",
        "Puts this record back into every total, chart and daily settlement it was taken out of.",
        "把這筆紀錄放回所有總額、圖表與每日結算。"
    );
    public static string TimelineExcludedCount => T(
        "excluded, not counted anywhere", "excluded, not counted anywhere",
        "excluded, not counted anywhere", "excluded, not counted anywhere",
        "excluded, not counted anywhere", "excluded, not counted anywhere",
        "筆已排除，不列入任何統計"
    );
    public static string TimelineExcludedRow => T(
        "excluded", "excluded", "excluded", "excluded", "excluded", "excluded", "已排除"
    );

    public static string WalletRetainerPrefix => T(
        "retainer:", "retainer:", "retainer:", "retainer:", "retainer:", "retainer:", "僱員："
    );

    // ─── Slash command help ───
    public static string CommandHelpMain => T(
        "Open GilDelta dashboard. /gildelta config opens settings, /gildelta wallets the balance list.",
        "Open GilDelta dashboard. /gildelta config opens settings, /gildelta wallets the balance list.",
        "Open GilDelta dashboard. /gildelta config opens settings, /gildelta wallets the balance list.",
        "Open GilDelta dashboard. /gildelta config opens settings, /gildelta wallets the balance list.",
        "Open GilDelta dashboard. /gildelta config opens settings, /gildelta wallets the balance list.",
        "Open GilDelta dashboard. /gildelta config opens settings, /gildelta wallets the balance list.",
        "開啟 GilDelta 儀表板。/gildelta config 可開啟設定，/gildelta wallets 可開啟餘額明細。"
    );
    public static string CommandHelpAlias => T(
        "Alias for /gildelta.", "Alias for /gildelta.", "Alias for /gildelta.",
        "Alias for /gildelta.", "Alias for /gildelta.", "Alias for /gildelta.",
        "/gildelta 的別名。"
    );

    // ─── Detail view ───
    public static string DetailTitle => T(
        "Details", "Details", "Details", "Details", "Details", "Details", "明細"
    );
    public static string DetailOpenHint => T(
        "(click to see the records behind this)",
        "(click to see the records behind this)",
        "(click to see the records behind this)",
        "(click to see the records behind this)",
        "(click to see the records behind this)",
        "(click to see the records behind this)",
        "（點一下看組成這個數字的明細）"
    );
    public static string DetailNothingPicked => T(
        "(pick a day, a bar or a category on the dashboard)",
        "(pick a day, a bar or a category on the dashboard)",
        "(pick a day, a bar or a category on the dashboard)",
        "(pick a day, a bar or a category on the dashboard)",
        "(pick a day, a bar or a category on the dashboard)",
        "(pick a day, a bar or a category on the dashboard)",
        "（在儀表板上點一天、一根長條或一個分類）"
    );
    public static string DetailNoRecords => T(
        "(no records are left behind this figure)",
        "(no records are left behind this figure)",
        "(no records are left behind this figure)",
        "(no records are left behind this figure)",
        "(no records are left behind this figure)",
        "(no records are left behind this figure)",
        "（這個數字背後已經沒有留下明細）"
    );
    public static string DetailSum => T(
        "detail total",
        "detail total",
        "detail total",
        "detail total",
        "detail total",
        "detail total",
        "明細合計"
    );
    public static string DetailRowsShown => T(
        "rows shown",
        "rows shown",
        "rows shown",
        "rows shown",
        "rows shown",
        "rows shown",
        "顯示筆數"
    );
    public static string DetailReconciles => T(
        "adds up to the figure that was clicked",
        "adds up to the figure that was clicked",
        "adds up to the figure that was clicked",
        "adds up to the figure that was clicked",
        "adds up to the figure that was clicked",
        "adds up to the figure that was clicked",
        "與剛才點的那個數字完全相符"
    );
    public static string DetailDifference => T(
        "differs from the figure that was clicked by",
        "differs from the figure that was clicked by",
        "differs from the figure that was clicked by",
        "differs from the figure that was clicked by",
        "differs from the figure that was clicked by",
        "differs from the figure that was clicked by",
        "與剛才點的那個數字相差"
    );
    public static string DetailDifferenceLedgerHelp => T(
        "That figure comes from the daily settlement book, which outlives the records behind it: once the retention setting has deleted a day's events the day itself still stands, and the detail below can only show what is left. Nothing has been miscounted - the settled figure is the one to trust.",
        "That figure comes from the daily settlement book, which outlives the records behind it: once the retention setting has deleted a day's events the day itself still stands, and the detail below can only show what is left. Nothing has been miscounted - the settled figure is the one to trust.",
        "That figure comes from the daily settlement book, which outlives the records behind it: once the retention setting has deleted a day's events the day itself still stands, and the detail below can only show what is left. Nothing has been miscounted - the settled figure is the one to trust.",
        "That figure comes from the daily settlement book, which outlives the records behind it: once the retention setting has deleted a day's events the day itself still stands, and the detail below can only show what is left. Nothing has been miscounted - the settled figure is the one to trust.",
        "That figure comes from the daily settlement book, which outlives the records behind it: once the retention setting has deleted a day's events the day itself still stands, and the detail below can only show what is left. Nothing has been miscounted - the settled figure is the one to trust.",
        "That figure comes from the daily settlement book, which outlives the records behind it: once the retention setting has deleted a day's events the day itself still stands, and the detail below can only show what is left. Nothing has been miscounted - the settled figure is the one to trust.",
        "上面那個數字來自每日結算，而結算比它背後的明細活得久：保留天數把那一天的明細清掉之後，那一天的結算仍然成立，下面就只能顯示還留著的那幾筆。這不是算錯——該相信的是結算那個數字。"
    );
    public static string DetailDifferenceHelp => T(
        "The records below do not add up to the figure that was clicked. Reopening the dashboard rebuilds both from the same log, so if it keeps happening it is worth reporting.",
        "The records below do not add up to the figure that was clicked. Reopening the dashboard rebuilds both from the same log, so if it keeps happening it is worth reporting.",
        "The records below do not add up to the figure that was clicked. Reopening the dashboard rebuilds both from the same log, so if it keeps happening it is worth reporting.",
        "The records below do not add up to the figure that was clicked. Reopening the dashboard rebuilds both from the same log, so if it keeps happening it is worth reporting.",
        "The records below do not add up to the figure that was clicked. Reopening the dashboard rebuilds both from the same log, so if it keeps happening it is worth reporting.",
        "The records below do not add up to the figure that was clicked. Reopening the dashboard rebuilds both from the same log, so if it keeps happening it is worth reporting.",
        "下面的明細加起來跟剛才點的數字對不上。重新開一次儀表板會把兩邊都從同一份紀錄重建；若一直如此，這值得回報。"
    );

    // ─── Timeline paging ───
    public static string TimelineShowMore => T(
        "show more",
        "show more",
        "show more",
        "show more",
        "show more",
        "show more",
        "顯示更多"
    );
    public static string TimelineShowMoreHelp => T(
        "The list starts at the newest 200 records. Each press adds another 200.",
        "The list starts at the newest 200 records. Each press adds another 200.",
        "The list starts at the newest 200 records. Each press adds another 200.",
        "The list starts at the newest 200 records. Each press adds another 200.",
        "The list starts at the newest 200 records. Each press adds another 200.",
        "The list starts at the newest 200 records. Each press adds another 200.",
        "這份清單先從最新的 200 筆開始，每按一次再多顯示 200 筆。"
    );

    // ─── Balances (the wallets behind the total, a dashboard tab) ───
    public static string BalanceTitle => T(
        "Balances", "Balances", "Balances", "Balances", "Balances", "Balances", "餘額明細"
    );
    public static string TotalClickHint => T(
        "(click the total to open the daily settlement)",
        "(click the total to open the daily settlement)",
        "(click the total to open the daily settlement)",
        "(click the total to open the daily settlement)",
        "(click the total to open the daily settlement)",
        "(click the total to open the daily settlement)",
        "（點一下總資產，開啟每日結算）"
    );
    public static string BalanceColumnAmount => T(
        "balance", "balance", "balance", "balance", "balance", "balance", "餘額"
    );
    public static string BalanceColumnReadAt => T(
        "read at", "read at", "read at", "read at", "read at", "read at", "讀到的時間"
    );
    public static string BalanceLiveNow => T(
        "now", "now", "now", "now", "now", "now", "現在"
    );
    public static string BalanceSumRow => T(
        "total", "total", "total", "total", "total", "total", "合計"
    );
    public static string BalanceNoWallets => T(
        "(no wallet balance to show yet)",
        "(no wallet balance to show yet)",
        "(no wallet balance to show yet)",
        "(no wallet balance to show yet)",
        "(no wallet balance to show yet)",
        "(no wallet balance to show yet)",
        "（目前沒有任何錢包餘額可以顯示）"
    );
    public static string BalanceStaleHelp => T(
        "This is not a live figure - it is the balance the last time this wallet was read. A character you are not logged in as keeps whatever was read the last time you played them, and a wallet the game did not answer for this time keeps its previous reading too.",
        "This is not a live figure - it is the balance the last time this wallet was read. A character you are not logged in as keeps whatever was read the last time you played them, and a wallet the game did not answer for this time keeps its previous reading too.",
        "This is not a live figure - it is the balance the last time this wallet was read. A character you are not logged in as keeps whatever was read the last time you played them, and a wallet the game did not answer for this time keeps its previous reading too.",
        "This is not a live figure - it is the balance the last time this wallet was read. A character you are not logged in as keeps whatever was read the last time you played them, and a wallet the game did not answer for this time keeps its previous reading too.",
        "This is not a live figure - it is the balance the last time this wallet was read. A character you are not logged in as keeps whatever was read the last time you played them, and a wallet the game did not answer for this time keeps its previous reading too.",
        "This is not a live figure - it is the balance the last time this wallet was read. A character you are not logged in as keeps whatever was read the last time you played them, and a wallet the game did not answer for this time keeps its previous reading too.",
        "這不是現在的數字，是上次讀到這個錢包時的餘額。沒有登入的角色會一直沿用上次遊玩時讀到的數字；這次遊戲沒有回報的錢包，也會沿用上一次的讀數。"
    );
    public static string BalanceReadAtUnknownHelp => T(
        "We do not know when this figure was read, so it shows as \"?\" rather than as a time we made up.",
        "We do not know when this figure was read, so it shows as \"?\" rather than as a time we made up.",
        "We do not know when this figure was read, so it shows as \"?\" rather than as a time we made up.",
        "We do not know when this figure was read, so it shows as \"?\" rather than as a time we made up.",
        "We do not know when this figure was read, so it shows as \"?\" rather than as a time we made up.",
        "We do not know when this figure was read, so it shows as \"?\" rather than as a time we made up.",
        "這個數字是什麼時候讀到的，我們並不知道，所以顯示「?」而不是隨便編一個時間。"
    );
    public static string BalanceUnreadableHelp => T(
        "The game did not give us this wallet's balance this time, so it shows as \"?\" instead of 0 and is left out of the total below.",
        "The game did not give us this wallet's balance this time, so it shows as \"?\" instead of 0 and is left out of the total below.",
        "The game did not give us this wallet's balance this time, so it shows as \"?\" instead of 0 and is left out of the total below.",
        "The game did not give us this wallet's balance this time, so it shows as \"?\" instead of 0 and is left out of the total below.",
        "The game did not give us this wallet's balance this time, so it shows as \"?\" instead of 0 and is left out of the total below.",
        "The game did not give us this wallet's balance this time, so it shows as \"?\" instead of 0 and is left out of the total below.",
        "這次遊戲沒有把這個錢包的餘額給我們，所以顯示「?」而不是 0，也沒有算進下面的合計。"
    );
    public static string BalanceUnknownCount => T(
        "wallet(s) could not be read and are not in the total above, so it is a lower bound",
        "wallet(s) could not be read and are not in the total above, so it is a lower bound",
        "wallet(s) could not be read and are not in the total above, so it is a lower bound",
        "wallet(s) could not be read and are not in the total above, so it is a lower bound",
        "wallet(s) could not be read and are not in the total above, so it is a lower bound",
        "wallet(s) could not be read and are not in the total above, so it is a lower bound",
        "個錢包讀不到，沒有算進上面的合計，所以那是下限"
    );
    public static string BalanceUnknownCountHelp => TTw(
        "A wallet with no figure contributes nothing to the total rather than contributing a 0 - counting an unreadable wallet as empty is what used to fabricate huge free company chest deposits. Walk to the chest, or log in as that character, and the row fills in. A free company chest has one extra wrinkle: while it really is empty, walking to it is not enough - the row stays \"?\" until the server has sent a non-zero balance for that company at least once since you logged in.",
        "讀不到的錢包對合計貢獻的是「什麼都沒有」，而不是 0——把讀不到當成 0，正是以前會憑空多出巨額公會儲物櫃存入的原因。走到儲物櫃旁邊、或登入那個角色，這一列就會補上。公會儲物櫃多了一種情況：它真的是空的時候，走到旁邊也還不夠——在本次登入收到過一次非零餘額之前，這一列會一直顯示「?」。"
    );

    /// <summary>A format string: {0} is how many wallets were read as 0.</summary>
    public static string BalanceHideZero => T(
        "Hide wallets with a 0 balance ({0})",
        "Hide wallets with a 0 balance ({0})",
        "Hide wallets with a 0 balance ({0})",
        "Hide wallets with a 0 balance ({0})",
        "Hide wallets with a 0 balance ({0})",
        "Hide wallets with a 0 balance ({0})",
        "隱藏餘額為 0 的錢包（{0}）"
    );
    public static string BalanceHideZeroHelp => TTw(
        "Leaves wallets whose balance is exactly 0 out of this list, including one still showing an earlier reading that was 0. A wallet that could not be read shows \"?\" - that is not 0, so it always stays listed. The number in brackets is how many wallets are at 0. Only this list changes: the total here and every other view stay exactly the same.",
        "餘額確定是 0 的錢包不列出來，沿用上次讀數、而那次讀到 0 的也算。讀不到的錢包顯示「?」，那不是 0，所以一律照樣列出。括號裡的數字是餘額為 0 的錢包有幾個。只影響這份清單：這裡的合計和其他畫面的數字都完全不變。"
    );
}
