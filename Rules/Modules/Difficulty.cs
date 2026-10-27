namespace GtfoPunk.Rules.Modules;

/// <summary>
/// Party difficulty, picked by the host in the lobby (the DIFFICULTY button next to READY FOR CAGE DROP) and sent to everyone.
///
/// | Level | Alarm doors (most scans) | Starting ammo, pack fill, items |
/// |---|---|---|
/// | I WARDEN'S PET | 0 — alarms become one plain scan, no wave | 100% |
/// | II EXPENDABLE ASSET | 1 | 80% |
/// | III PRISONER | 2 | 60% |
/// | IV SURVIVAL ODDS: LOW | 3 | 40% |
/// | V THE COMPLEX IS HUNGRY | 4 | 20% |
/// | VI GET THE F*** OUT | 5 | 0% (melee only) |
/// | DEFAULT — AS THE WARDEN INTENDED | as in the game | as in the game, no loadout, no gadgets, no infection growth, artifacts stay |
///
/// Modules read <see cref="Level"/> / <see cref="Vanilla"/> / <see cref="Share"/> and react to <see cref="Changed"/>.
/// New gear (sentries, the silenced pistol) stays available in every mode.
/// </summary>
public class Difficulty : ModuleBase
{
    public override string Id => "Difficulty";
    public override string Description => "Party difficulty (I–VI or DEFAULT) picked by the host in the lobby: alarm doors, starting ammo, pack and item amounts. Everyone needs it.";
    public override ModuleSide Side => ModuleSide.Everyone;

    public const int Default = 0, Max = 6;

    /// <summary>0 — DEFAULT (the game's own rules), 1..6 — levels I..VI.</summary>
    public static int Level { get; private set; } = 3;
    public static bool Vanilla => Level == Default;
    /// <summary>Starting ammo / pack fill / item share: 1, 0.8 … 0 for I..VI; 1 in DEFAULT (where the modules don't give anything anyway).</summary>
    public static float Share => Vanilla ? 1f : (Max - Level) / 5f;
    /// <summary>Most scans an alarm door asks for: 0..5 for I..VI; -1 — as in the game.</summary>
    public static int AlarmCap => Vanilla ? -1 : Level - 1;
    /// <summary>Alarm mode: false — a cap on the scans (AlarmCap); true — every alarm shifted by AlarmShift scans.</summary>
    public static bool ShiftMode { get; private set; } = true;
    /// <summary>Scans added to every alarm in shift mode: −4, −3, −2, −1, 0, +1 for I..VI.</summary>
    public static int AlarmShift => Vanilla ? 0 : Level - 5;

    public static event Action<int> Changed;

    private static ConfigEntry<int> _level, _mode;

    protected override void OnBindConfig()
    {
        _level = Bind("Level", 3, "Difficulty the host plays with: 1–6 (I WARDEN'S PET … VI GET THE F*** OUT), 0 — DEFAULT (the game's rules). Changed with the DIFFICULTY button in the lobby.");
        Level = Math.Clamp(_level.Value, Default, Max);
        _mode = Bind("AlarmMode", 1, "How the difficulty changes alarm doors (the host's setting is used by everyone): "
            + "0 — a cap: at most N scans (I — one plain scan, II — 1 … VI — 5); "
            + "1 — a shift: every alarm loses scans (I −4, II −3, III −2, IV −1, V as in the game, VI +1), so a Class V door stays harder than a Class II one; 0 scans or fewer — one plain scan without a wave.");
        ShiftMode = _mode.Value != 0;
    }

    protected override void OnEnable()
    {
        PartyRules.Register("Difficulty.Level", () => Lang.T("Difficulty", "Сложность"), Name, Default, Max, _level, Apply, Describe);
        PartyRules.Register("Difficulty.AlarmMode", () => "Alarm mode", v => v == 0 ? "cap" : "shift", 0, 1, _mode, ApplyMode, hidden: true);
    }

    private static void Apply(int level)
    {
        level = Math.Clamp(level, Default, Max);
        if (level == Level) return;
        Level = level;
        ModInfo.Log.LogInfo($"[Difficulty] {Name(level)}: alarm cap {AlarmCap}, share {Share:0.##}");
        foreach (Action<int> h in Changed?.GetInvocationList() ?? Array.Empty<Delegate>())
            try { h(level); } catch (Exception e) { ModInfo.Log.LogWarning($"[Difficulty] {h.Method.DeclaringType?.Name}: {e.Message}"); }
    }

    private static void ApplyMode(int mode)
    {
        bool shift = mode != 0;
        if (shift == ShiftMode) return;
        ShiftMode = shift;
        ModInfo.Log.LogInfo($"[Difficulty] alarm mode: {(shift ? "shift" : "cap")}");
        foreach (Action<int> h in Changed?.GetInvocationList() ?? Array.Empty<Delegate>())
            try { h(Level); } catch (Exception e) { ModInfo.Log.LogWarning($"[Difficulty] {h.Method.DeclaringType?.Name}: {e.Message}"); }
    }

    public static string Name(int level) => level switch
    {
        1 => Lang.T("Warden's pet (I)", "Любимчик Смотрителя (I)"),
        2 => Lang.T("Expendable asset (II)", "Расходный материал (II)"),
        3 => Lang.T("Prisoner (III)", "Заключённый (III)"),
        4 => Lang.T("Survival odds: low (IV)", "Шансы выжить: низкие (IV)"),
        5 => Lang.T("The Complex is hungry (V)", "Комплекс голоден (V)"),
        6 => Lang.T("Get the f*** out (VI)", "Вали отсюда на… (VI)"),
        _ => Lang.T("As the Warden intended (default)", "Как задумал Смотритель (по умолчанию)"),
    };

    /// <summary>Two short lines shown next to the lobby button.</summary>
    public static string Describe(int level)
    {
        if (level <= Default) return Lang.T("Alarms and ammo as in the game\nNo loadout, no gadgets", "Тревоги и патроны как в игре\nБез снаряжения и гаджетов");
        int scans = level - 1, pct = (Max - level) * 20, shift = level - 5;
        string alarm = ShiftMode
            ? shift == 0 ? Lang.T("Alarms as in the game", "Тревоги как в игре")
              : shift > 0 ? Lang.T($"Alarms: +{shift} scan", $"Тревоги: +{shift} скан")
              : Lang.T($"Alarms: {shift} scans (none left — plain scan)", $"Тревоги: {shift} скана (не осталось — обычный скан)")
            : scans == 0 ? Lang.T("Alarms: one plain scan, no wave", "Тревоги: один обычный скан, без волны")
                         : Lang.T($"Alarm doors: up to {scans} scan{(scans > 1 ? "s" : "")}", $"Двери с тревогой: до {scans} скан{(scans == 1 ? "а" : "ов")}");
        string res = pct == 0 ? Lang.T("No ammo, packs or items — melee only", "Без патронов, пачек и предметов — только милишка")
                              : Lang.T($"Ammo, pack and items: {pct}%", $"Патроны, пачка и предметы: {pct}%");
        return alarm + "\n" + res;
    }

    /// <summary>A stack scaled by the difficulty share, rounded (half up); below one — nothing.</summary>
    public static int Scale(int full) => (int)Math.Round(full * Share, MidpointRounding.AwayFromZero);
}
