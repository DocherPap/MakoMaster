using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace MakoMaster;

public sealed partial class Plugin
{
    // Complete Talk event context used by TextAdvance/ECommons. No OS mouse input is emitted.
    // https://github.com/NightmareXIV/ECommons/blob/master/ECommons/UIHelpers/AddonMasterImplementations/Talk.cs
    // https://github.com/NightmareXIV/ECommons/blob/master/ECommons/UIHelpers/AddonMasterImplementations/!AddonMasterBase.cs
    private unsafe bool AdvanceTalkControl(AtkUnitBase* unit)
    {
        if (unit == null || FindWorldAddon("Talk") != unit || keyboard.Holding || !NativeInput.ForegroundIsGame) return false;
        if (!WorldDialogueRules.CanHandle(ownDialogue, lastInteractedNpc, targetManager.Target?.BaseId)) return false;
        var speaker = WorldDialogueRules.Speaker(lastInteractedNpc);
        if (speaker.Length == 0 || ReadNamedAddon("Talk")?.Nodes.Any(n => n.Visible && n.Text == speaker) != true) return false;
        var stage = AtkStage.Instance();
        if (stage == null || stage->AtkEventTarget.VirtualTable == null) return false;
        AtkEvent ev = default;
        ev.Listener = &unit->AtkEventListener;
        ev.Target = &stage->AtkEventTarget;
        ev.State.StateFlags = (AtkEventStateFlags)0x84;
        AtkEventData data = default;
        var id = unit->Id;
        WriteRecord(new { Kind = "WorldDialogueInternalAction", At = DateTimeOffset.Now, Addon = "Talk", Npc = lastInteractedNpc,
            Action = "AdvanceTalk", HasStageTarget = true });
        foreach (var type in new[] { AtkEventType.MouseDown, AtkEventType.MouseClick, AtkEventType.MouseUp })
        {
            var current = FindWorldAddon("Talk");
            if (current != unit || current->Id != id) break;
            current->ReceiveEvent(type, 0, &ev, &data);
        }
        return true;
    }
    private unsafe bool ConfirmYesControl(AtkUnitBase* unit)
    {
        if (unit == null || FindWorldAddon("SelectYesno") != unit || keyboard.Holding || !NativeInput.ForegroundIsGame) return false;
        var addon = (AddonSelectYesno*)unit;
        var button = addon->YesButton;
        if (button == null || !button->IsEnabled || button->OwnerNode == null || button->ButtonTextNode == null
            || !button->OwnerNode->AtkResNode.IsVisible() || SeString.Parse(button->ButtonTextNode->NodeText.AsSpan()).TextValue != "是") return false;
        var prompt = addon->PromptText == null ? "" : SeString.Parse(addon->PromptText->NodeText.AsSpan()).TextValue;
        if (!WorldDialogueRules.CanConfirm(ownDialogue, lastInteractedNpc, targetManager.Target?.BaseId, prompt, pendingPurchase)) return false;
        // ECommons ClickAddonButton invokes the button's live registered event. Never substitute an empty event.
        // https://github.com/NightmareXIV/ECommons/blob/master/ECommons/Automation/UIInput/ClickHelper.cs
        var seen = new HashSet<nint>();
        for (var e = button->OwnerNode->AtkResNode.AtkEventManager.Event; e != null && seen.Count < 64 && seen.Add((nint)e); e = e->NextEvent)
        {
            if (e->State.EventType != AtkEventType.ButtonClick || e->Listener != &unit->AtkEventListener
                || e->Target == null || e->Target->VirtualTable == null || e->Param > int.MaxValue) continue;
            WriteRecord(new { Kind = "WorldDialogueInternalAction", At = DateTimeOffset.Now, Addon = "SelectYesno", Npc = lastInteractedNpc,
                Action = "Yes", EventParam = e->Param, HasLiveTarget = true });
            unit->ReceiveEvent(e->State.EventType, (int)e->Param, e);
            return true;
        }
        RecordWorldDialogueWait("“是”按钮的内部事件尚未就绪");
        return false;
    }
}
