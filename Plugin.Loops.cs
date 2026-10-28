using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace MakoMaster;

public sealed partial class Plugin
{
    private readonly IObjectTable objects;
    private readonly ITargetManager targetManager;
    private readonly IClientState clientState;
    private readonly IAddonLifecycle addonLifecycle;
    private bool loopEnabled;
    private int loopCount = 1;
    private RoundLoop? roundLoop;
    private uint loopTerritory, loopNpcDataId;
    private string loopNpcName = "", loopMessage = "";
    private readonly SkillPauseGate skillPause = new();
    private bool skillPaused;
    private long lastSkillPauseTick;
    private readonly HashSet<string> interruptibleSkills = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Skill, long At)> lastCasts = new();
    private static readonly string[] diagnosticAddons = ["KTGEnterDialogue", "ContentsFinderConfirm", "KTGResult"];
    private string InterruptibleFile => Path.Combine(pi.AssemblyLocation.DirectoryName!, "interruptible-skills.json");

    private void InitializeDiagnostics()
    {
        try
        {
            if (File.Exists(InterruptibleFile)) interruptibleSkills.UnionWith(JsonSerializer.Deserialize<string[]>(File.ReadAllText(InterruptibleFile)) ?? []);
        }
        catch (Exception ex) { log.Error(ex, "MakoMaster skill observations could not be read"); }
        addonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, diagnosticAddons, OnUiEvent);
    }
    private void DisposeDiagnostics() => addonLifecycle.UnregisterListener(AddonEvent.PreReceiveEvent, diagnosticAddons, OnUiEvent);

    private unsafe void OnUiEvent(AddonEvent type, AddonArgs args)
    {
        if (capture == null || args is not AddonReceiveEventArgs ev || (byte)ev.AtkEventType != (byte)AtkEventType.ButtonClick) return;
        var native = (AtkEvent*)ev.AtkEvent;
        WriteRecord(new { Kind = "UiButtonEvent", At = DateTimeOffset.Now, Addon = args.AddonName,
            EventType = (byte)ev.AtkEventType, ev.EventParam, NodeId = native == null || native->Node == null ? (uint?)null : native->Node->NodeId });
    }

    // Identified from the entry NPC used for the captured registration flow.
    private const uint EntryNpcBaseId = 1058751;

    private Dalamud.Game.ClientState.Objects.Types.IGameObject? FindEntryNpc()
    {
        var player = objects.LocalPlayer;
        if (player == null) return null;
        return objects.Where(o => o.IsValid() && o.IsTargetable && o.ObjectKind == ObjectKind.EventNpc
                && o.BaseId == EntryNpcBaseId)
            .OrderBy(o => Vector3.DistanceSquared(player.Position, o.Position)).FirstOrDefault();
    }

    private void PrepareLoop()
    {
        if (loopCount is < 1 or > 9999) throw new InvalidDataException("循环次数应为 1～9999。");
        if (!clientState.IsLoggedIn || objects.LocalPlayer == null)
            throw new InvalidDataException("请进入游戏中的魔光键影入场区域后启动循环。");
        var npc = FindEntryNpc();
        loopTerritory = clientState.TerritoryType;
        loopNpcDataId = EntryNpcBaseId;
        loopNpcName = npc?.Name.TextValue ?? "";
        roundLoop = new(loopCount);
        loopMessage = npc == null ? "正在寻找魔光键影入场 NPC，请靠近入场区域。" : "";
    }
    // Returns true while menus / scene transitions own input, or after the finite loop stops.
    private unsafe bool UpdateRoundLoop(long now)
    {
        if (roundLoop == null) return false;
        var results = ReadNamedAddon("KTGResult");
        var entry = ReadNamedAddon("KTGEnterDialogue");
        var confirm = ReadNamedAddon("ContentsFinderConfirm");
        var game = ReadNamedAddon("KTGInput") != null || ReadNamedAddon("KTGEWord") != null || ReadNamedAddon("_KTGCountDown") != null;
        var loading = !clientState.IsLoggedIn || objects.LocalPlayer == null || ReadNamedAddon("NowLoading") != null;
        var home = clientState.TerritoryType == loopTerritory && !loading;
        var validConfirm = confirm?.Nodes.Any(n => n.Visible && n.Text == "魔光键影") == true;
        var validEntry = entry?.Nodes.Any(n => n.Visible && n.Text == "魔光键影") == true;
        if (confirm != null && !validConfirm)
        { loopMessage = "等待处理其他任务的出发确认。"; return true; }
        var view = new RoundView(home, loading, game, validEntry, validConfirm, results != null);
        var oldCompleted = roundLoop.Completed;
        var action = roundLoop.Tick(view, now);
        if (oldCompleted != roundLoop.Completed)
            WriteRecord(new { Kind = "RoundCompleted", At = DateTimeOffset.Now, roundLoop.Completed, roundLoop.Limit });
        if (action == RoundAction.Finish)
        { StopTrial(roundLoop.Status); return true; }
        if (keyboard.Holding) return true;
        var acted = false;
        loopMessage = "";
        if (action == RoundAction.Interact)
        {
            var npc = FindEntryNpc();
            var player = objects.LocalPlayer;
            if (npc != null && player != null)
            {
                loopNpcName = npc.Name.TextValue;
                if (targetManager.Target?.GameObjectId != npc.GameObjectId) targetManager.Target = npc;
            }
            if (npc == null || player == null) loopMessage = "正在寻找魔光键影入场 NPC，请靠近入场区域。";
            else if (Vector3.Distance(player.Position, npc.Position) > 6) loopMessage = "距离入场 NPC 太远，请靠近后继续。";
            else if (ReadNamedAddon("Talk") != null || ReadNamedAddon("SelectString") != null || ReadNamedAddon("SelectYesno") != null)
                loopMessage = "等待关闭当前对话；当前菜单需记录后适配。";
            else
            {
                var system = TargetSystem.Instance();
                if (system != null)
                {
                    WriteRecord(new { Kind = "LoopInteraction", At = DateTimeOffset.Now, Npc = loopNpcName, DataId = loopNpcDataId });
                    targetManager.Target = npc;
                    system->InteractWithObject((GameObject*)npc.Address, false);
                    acted = true;
                }
            }
        }
        else if (action is RoundAction.Apply or RoundAction.Accept or RoundAction.Exit)
        {
            var name = action == RoundAction.Apply ? "KTGEnterDialogue" : action == RoundAction.Accept ? "ContentsFinderConfirm" : "KTGResult";
            var label = action == RoundAction.Apply ? "申请参加" : action == RoundAction.Accept ? "出发" : "退出";
            var id = action == RoundAction.Apply ? 9u : action == RoundAction.Accept ? 63u : 48u;
            acted = ClickRegisteredButton(name, id, label);
            if (!acted) loopMessage = $"等待 {name} 的“{label}”按钮就绪；未识别时请保留日志。";
        }
        if (acted)
        {
            roundLoop.ActionSent(action, now);
            WriteRecord(new { Kind = "LoopAction", At = DateTimeOffset.Now, Action = action.ToString(), roundLoop.Completed, roundLoop.Limit });
        }
        return !game || results != null || loading;
    }

    private unsafe bool ClickRegisteredButton(string name, uint nodeId, string label)
    {
        var manager = FFXIVClientStructs.FFXIV.Client.UI.RaptureAtkUnitManager.Instance();
        if (manager == null) return false;
        var list = &manager->AllLoadedUnitsList;
        for (var i = 0; i < Math.Min((int)list->Count, list->Entries.Length); i++)
        {
            var addon = list->Entries[i].Value;
            if (addon == null || !addon->IsVisible || addon->NameString != name) continue;
            var node = addon->GetNodeById(nodeId);
            if (node == null || !node->IsVisible()) return false;
            var button = node->GetAsAtkComponentButton();
            if (button == null || !button->IsEnabled || button->ButtonTextNode == null
                || Dalamud.Game.Text.SeStringHandling.SeString.Parse(button->ButtonTextNode->NodeText.AsSpan()).TextValue != label) return false;
            // Resolve the live registered event instead of guessing an addon callback id.
            var e = node->AtkEventManager.Event;
            var visited = new HashSet<nint>();
            for (var count = 0; e != null && count < 64 && visited.Add((nint)e); count++, e = e->NextEvent)
            {
                if (e->State.EventType != AtkEventType.ButtonClick || e->Listener != &addon->AtkEventListener || e->Param > int.MaxValue) continue;
                var copy = *e;
                copy.NextEvent = null;
                copy.State.StateFlags = AtkEventStateFlags.None;
                copy.State.ReturnFlags = 0;
                AtkEventData data = default;
                WriteRecord(new { Kind = "RegisteredButton", At = DateTimeOffset.Now, Addon = name, NodeId = nodeId, Param = e->Param, Label = label });
                e->Listener->ReceiveEvent(AtkEventType.ButtonClick, (int)e->Param, &copy, &data);
                return true;
            }
            return false;
        }
        return false;
    }

    private void ObserveInterrupts(AddonSnapshot? casting)
    {
        if (casting == null) { lastCasts.Clear(); return; }
        var now = Environment.TickCount64;
        foreach (var row in casting.Nodes.Where(n => n.Visible && n.NodeId == 4 && n.Text.Length > 0))
            lastCasts[WordMatching.Slot(row.Path)] = (row.Text, now);
        foreach (var row in casting.Nodes.Where(n => n.Visible && n.NodeId == 3 && n.Text == "打断成功！"))
        {
            if (!lastCasts.TryGetValue(WordMatching.Slot(row.Path), out var previous) || now - previous.At > 3000 || !interruptibleSkills.Add(previous.Skill)) continue;
            if (saveLogs)
            {
                var temporary = InterruptibleFile + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(interruptibleSkills.Order().ToArray()));
                File.Move(temporary, InterruptibleFile, true);
            }
            WriteRecord(new { Kind = "InterruptibleSkillConfirmed", At = DateTimeOffset.Now, previous.Skill });
        }
    }

    private bool UpdateSkillPause(AddonSnapshot? casting, long now)
    {
        var casts = casting?.Nodes.Where(n => n.Visible && n.NodeId == 4 && n.Text.Length > 0).Select(n =>
        {
            var slot = WordMatching.Slot(n.Path);
            var countdown = casting.Nodes.FirstOrDefault(r => r.Visible && r.NodeId == 5 && WordMatching.Slot(r.Path) == slot)?.Text;
            // Integer / hidden countdowns cannot place an exact half-second boundary.
            double? seconds = countdown != null && countdown.Contains('.') && double.TryParse(countdown, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
            var fill = casting.Visuals?.FirstOrDefault(v => v.NodeId == 6 && v.Visible && v.Type == "Image" && WordMatching.Slot(v.Path) == slot);
            return new CastObservation(slot, n.Text, seconds, fill?.ScaleX);
        }).ToArray() ?? [];
        var paused = skillPause.Tick(casts, now);
        if (skillPaused) trial?.ExtendWait(Math.Max(0, now - lastSkillPauseTick));
        if (paused != skillPaused)
            WriteRecord(new { Kind = "SkillPause", At = DateTimeOffset.Now, Paused = paused, skillPause.AwaitingProgress, Casts = casts });
        if (paused) keyboard.Release();
        skillPaused = paused; lastSkillPauseTick = now;
        return paused;
    }

    private static Dictionary<string, bool> ReadCastKinds(AddonSnapshot? casting)
    {
        var kinds = new Dictionary<string, bool>();
        foreach (var row in casting?.Nodes.Where(n => n.Visible && n.NodeId == 4 && n.Text.Length > 0) ?? [])
        {
            var slot = WordMatching.Slot(row.Path);
            if (WordMatching.KnownInterruptibleSkill(row.Text)) { kinds[slot] = true; continue; }
            // Other skills use the current countdown/interrupt marker.
            var marker = casting!.Visuals?.FirstOrDefault(v => v.NodeId == 7 && WordMatching.Slot(v.Path) == slot);
            var countdown = casting.Nodes.FirstOrDefault(n => n.NodeId == 5 && WordMatching.Slot(n.Path) == slot);
            if (marker != null || countdown != null) kinds[slot] = marker?.Visible == true || countdown?.Visible == true;
        }
        return kinds;
    }

    private sealed record VisualRow(string Path, uint NodeId, string Type, bool Visible, uint Color,
        byte MultiplyRed, byte MultiplyGreen, byte MultiplyBlue, float X, float Y, ushort Width, ushort Height,
        float ScaleX, float ScaleY, ushort? PartId, ushort? TimelineLabel, float? TimelineFrame, ushort? TimelineEnd, List<NodeEventRow> Events);
    private sealed record NodeEventRow(string Type, uint Param, string Listener);

    private static unsafe List<VisualRow>? ReadVisuals(AtkUnitBase* addon, string name)
    {
        if (name is not ("KTGEWord" or "KTGEnemyCastBar" or "KTGEnterDialogue" or "ContentsFinderConfirm" or "KTGResult")) return null;
        var rows = new List<VisualRow>();
        WalkVisuals(&addon->UldManager, "", true, 0, new(), rows, &addon->AtkEventListener);
        return rows;
    }
    private static unsafe void WalkVisuals(AtkUldManager* uld, string prefix, bool inherited, int depth,
        HashSet<nint> visited, List<VisualRow> rows, AtkEventListener* owner)
    {
        if (uld == null || uld->NodeList == null || depth > 12) return;
        for (var i = 0; i < Math.Min((int)uld->NodeListCount, 4096); i++)
        {
            var node = uld->NodeList[i];
            if (node == null || visited.Count >= 16384 || !visited.Add((nint)node)) continue;
            var path = prefix + "/" + node->NodeId + "[" + i + "]";
            var visible = inherited && node->IsVisible();
            var parent = node->ParentNode; var ancestors = 0;
            while (visible && parent != null && ancestors++ < 64) { visible &= parent->IsVisible(); parent = parent->ParentNode; }
            if (parent != null && ancestors >= 64) visible = false;
            var events = new List<NodeEventRow>();
            var e = node->AtkEventManager.Event; var eventSeen = new HashSet<nint>();
            for (var count = 0; e != null && count < 64 && eventSeen.Add((nint)e); count++, e = e->NextEvent)
                events.Add(new(e->State.EventType.ToString(), e->Param, e->Listener == owner ? "Addon" : "Other"));
            rows.Add(new(path, node->NodeId, node->Type.ToString(), visible, node->Color.RGBA,
                node->MultiplyRed, node->MultiplyGreen, node->MultiplyBlue, node->ScreenX, node->ScreenY,
                node->Width, node->Height, node->ScaleX, node->ScaleY,
                node->Type == NodeType.Image ? ((AtkImageNode*)node)->PartId : null,
                node->Timeline == null ? null : node->Timeline->ActiveLabelId,
                node->Timeline == null || !float.IsFinite(node->Timeline->FrameTime) ? null : node->Timeline->FrameTime,
                node->Timeline == null ? null : node->Timeline->LabelEndFrameIdx, events));
            if ((int)node->Type >= 1000)
            {
                var component = ((AtkComponentNode*)node)->Component;
                if (component != null) WalkVisuals(&component->UldManager, path, visible, depth + 1, visited, rows, owner);
            }
        }
    }
}
