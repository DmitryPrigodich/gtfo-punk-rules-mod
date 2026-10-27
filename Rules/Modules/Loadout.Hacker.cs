using LevelGeneration;
using Player;
using SNetwork;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// The hacking tool is a gadget: only who picked it (GADGET = Hacking tool) can use it; the others can't take it out
/// (it's put away at once, with a note) — a lock melter still works. With the gadget, N takes it out.
/// The restriction holds only while some human player in the party has the gadget — otherwise (solo without it, or
/// nobody picked it) everyone hacks as in the game, so hackable locks never block the way (bots can't hack).
/// Looking at a hackable lock without the tool shows "HACKING TOOL NEEDED" right away.
/// In DEFAULT difficulty everyone has it, as in the game.
/// </summary>
public partial class Loadout
{
    private static float _nextHackNote, _nextHackCheck, _nextLockLook, _hackMsgUntil = -1f;
    private static bool _restricted;
    private static string _hackerName;
    private static bool _restrictLogged;

    /// <summary>Human players in the party who picked the hacking tool (bots don't count — they can't hack).</summary>
    private static void UpdateHackRestriction()
    {
        if (Time.time < _nextHackCheck) return;
        _nextHackCheck = Time.time + 1f;
        string name = null;
        try
        {
            foreach (var p in SNet.Slots.SlottedPlayers)
            {
                if (p == null || p.IsBot) continue;
                Choice c = p.IsLocal ? Local() : _remote.TryGetValue(p.Lookup, out var r) ? r : null;
                if (c?.Perk == PerkKind.Hacker) { name = p.NickName; break; }
            }
        }
        catch { }
        bool restricted = name != null;
        if (restricted != _restricted || !_restrictLogged)
        {
            _restrictLogged = true;
            ModInfo.Log.LogInfo(restricted ? $"[Loadout] hacking tool: only for its owner ({name})" : "[Loadout] hacking tool: nobody picked it — everyone can hack");
        }
        _restricted = restricted;
        _hackerName = name;
    }

    private static void HackerTick(bool inLevel, bool hacker)
    {
        if (!inLevel || Difficulty.Vanilla) return;
        var agent = GameUtil.LocalAgent;
        var inv = agent?.Inventory;
        if (agent == null || inv == null || !agent.Alive) return;
        UpdateHackRestriction();

        if (hacker)
        {
            if (GadgetKeyDown() && !ChatOpen() && inv.WieldedSlot != InventorySlot.HackingTool)
                agent.Sync.WantsToWieldSlot(InventorySlot.HackingTool);
            return;
        }
        if (!_restricted) return;
        LockLook(agent);
        if (inv.WieldedSlot != InventorySlot.HackingTool) return;
        agent.Sync.WantsToWieldSlot(InventorySlot.GearStandard);
        if (Time.time < _nextHackNote) return;
        _nextHackNote = Time.time + 10f;
        try
        {
            PlayerChatManager.PostChatMessageLocaly(SNet.LocalPlayer,
                $"<color=#ffd34f>{Lang.T($"No hacking tool: {_hackerName} has it. Or use a lock melter.", $"Нет инструмента взлома: он у {_hackerName}. Или используй выжигатель замков.")}</color>");
        }
        catch { }
        ModInfo.Log.LogInfo("[Loadout] hacking tool put away (not my gadget)");
    }

    /// <summary>Looking at a locked hackable lock without the tool: a message instead of a useless hack attempt.</summary>
    private static void LockLook(PlayerAgent agent)
    {
        var gui = GuiManager.InteractionLayer;
        if (Time.time >= _nextLockLook)
        {
            _nextLockLook = Time.time + 0.2f;
            bool looking = false;
            try
            {
                var cam = agent.FPSCamera;
                var go = cam != null ? cam.CameraRayObject : null;
                if (go != null && cam.CameraRayDist < 3f)
                {
                    var lk = go.GetComponentInParent<LG_WeakLock>();
                    looking = lk != null && lk.m_lockType == eWeakLockType.Hackable && lk.Status == eWeakLockStatus.LockedHackable;
                }
            }
            catch { }
            if (looking)
            {
                gui.SetMessage(Lang.T($"HACKING TOOL NEEDED — {_hackerName} has it (or a lock melter)", $"НУЖЕН ИНСТРУМЕНТ ВЗЛОМА — он у {_hackerName} (или выжигатель замков)"), ePUIMessageStyle.Default, 10);
                gui.MessageVisible = true;
                _hackMsgUntil = Time.time + 0.4f;
            }
        }
        if (_hackMsgUntil > 0f && Time.time > _hackMsgUntil) { _hackMsgUntil = -1f; gui.MessageVisible = false; }
    }
}
