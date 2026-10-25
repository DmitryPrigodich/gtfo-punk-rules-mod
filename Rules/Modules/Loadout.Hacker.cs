using Player;
using UnityEngine;

namespace GtfoPunk.Rules.Modules;

/// <summary>
/// The hacking tool is a gadget now: only who picked it (GADGET = Hacking tool) can use it; the others can't take it
/// out (it's put away at once, with a note) and break locks the old way. With the gadget, N takes it out.
/// In DEFAULT difficulty everyone has it, as in the game.
/// </summary>
public partial class Loadout
{
    private static float _nextHackNote;

    private static void HackerTick(bool inLevel, bool hacker)
    {
        if (!inLevel || Difficulty.Vanilla) return;
        var agent = GameUtil.LocalAgent;
        var inv = agent?.Inventory;
        if (agent == null || inv == null || !agent.Alive) return;

        if (hacker)
        {
            if (GadgetKeyDown() && !ChatOpen() && inv.WieldedSlot != InventorySlot.HackingTool)
                agent.Sync.WantsToWieldSlot(InventorySlot.HackingTool);
            return;
        }
        if (inv.WieldedSlot != InventorySlot.HackingTool) return;
        agent.Sync.WantsToWieldSlot(InventorySlot.GearStandard);
        if (Time.time < _nextHackNote) return;
        _nextHackNote = Time.time + 10f;
        try
        {
            PlayerChatManager.PostChatMessageLocaly(SNetwork.SNet.LocalPlayer,
                $"<color=#ffd34f>{Lang.T("No hacking tool: it's a gadget (pick it in the lobby). Break the lock instead.", "Нет инструмента взлома: это гаджет (выбирается в лобби). Замок — ломать.")}</color>");
        }
        catch { }
        ModInfo.Log.LogInfo("[Loadout] hacking tool put away (not my gadget)");
    }
}
