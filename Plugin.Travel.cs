using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace MakoMaster;

public sealed partial class Plugin
{
    private bool manualTravel, autoRedeem, redeemRunning, redeemReturning, ownTeleport, ownDialogue, ownShop;
    private bool worldOwnsInput, worldFault;
    private int redeemThreshold = 9100, pendingPurchase, purchaseBeforeCount;
    private uint purchaseBeforePoints, lastInteractedNpc;
    private long nextWorldTick, nextWorldAction, teleportAt, navigationAt, purchaseAt, nextWorldRecord;
    private uint navigationTerritory;
    private uint manualTravelNpc = EntryNpcBaseId;
    private Vector3 navigationGoal;
    private string worldMessage = "";
    private readonly NavigationRequestGate navigation = new();
    private bool WorldBusy => manualTravel || redeemRunning || redeemReturning || navigation.Owned || ownTeleport;
    private bool InKeyPlaza => clientState.IsLoggedIn && objects.LocalPlayer != null && clientState.TerritoryType == WorldAutomationRules.Plaza;

    private void DrawWorldControls()
    {
        var dependencyError = GetTravelDependencyError();
        ImGui.BeginDisabled(trial != null || OpeningChests || WorldBusy || worldFault || dependencyError != null);
        if (ImGui.Button("前往报名npc", new Vector2(ControlButtonWidth, 0))) OnCommand("", "travel-plaza");
        DependencyTooltip(dependencyError);
        ImGui.SameLine();
        if (ImGui.Button("前往兑换npc", new Vector2(ControlButtonWidth, 0))) OnCommand("", "travel-vendor");
        DependencyTooltip(dependencyError);
        ImGui.EndDisabled();
        if (worldMessage.Length > 0) ImGui.TextWrapped(worldMessage);
    }
    private void DrawAutoRedeemSettings()
    {
        var dependencyError = GetDependencyError(true, false);
        var unavailable = !InKeyPlaza || dependencyError != null;
        ImGui.BeginGroup();
        ImGui.BeginDisabled(unavailable);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, Vector2.Zero);
        var visible = ImGui.BeginTable("##autoRedeemLayout", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings);
        ImGui.PopStyleVar();
        if (visible)
        {
            ImGui.TableSetupColumn("##toggle", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.X);
            ImGui.TableSetupColumn("##description", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow(); ImGui.TableNextColumn();
            var enabled = autoRedeem;
            if (ImGui.Checkbox("##autoRedeem", ref enabled)) OnCommand("", enabled ? "redeem-on" : "redeem-off");
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            var columnStart = ImGui.GetCursorScreenPos().X;
            var columnWidth = ImGui.GetContentRegionAvail().X;
            const string prefix = "在自动排本时超过";
            ImGui.TextWrapped(prefix);
            if (ImGui.CalcTextSize(prefix).X + ImGui.GetStyle().ItemSpacing.X + NumberInputWidth(5) <= columnWidth) ImGui.SameLine();
            ImGui.SetNextItemWidth(NumberInputWidth(5));
            ImGui.BeginDisabled(redeemRunning);
            ImGui.InputInt("##redeemThreshold", ref redeemThreshold, 0, 0);
            ImGui.EndDisabled();
            const string suffix = "点数后自动兑换宝箱";
            if (ImGui.GetItemRectMax().X - columnStart + ImGui.GetStyle().ItemSpacing.X + ImGui.CalcTextSize(suffix).X <= columnWidth)
                ImGui.SameLine();
            ImGui.TextWrapped(suffix);
            ImGui.EndTable();
        }
        ImGui.EndDisabled();
        ImGui.EndGroup();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("只有在键斗广场时可以启用。" + (dependencyError == null ? "" : "\n" + dependencyError));
    }
    private void StartPlazaTravel(uint destinationNpc = EntryNpcBaseId)
    {
        if (trial != null || OpeningChests || WorldBusy) throw new InvalidDataException("请先停止当前操作。");
        if (!clientState.IsLoggedIn || objects.LocalPlayer == null) throw new InvalidDataException("请先登录游戏。");
        if (IsMinigameInProgress()) throw new InvalidDataException("请先退出当前游戏，再前往键斗广场。");
        if (GetTravelDependencyError() is { } dependencyError) throw new InvalidDataException(dependencyError);
        manualTravelNpc = destinationNpc;
        manualTravel = true; worldFault = false; nextWorldTick = nextWorldAction = 0;
        worldMessage = "准备前往键斗广场。";
    }
    private void StopWorldAutomation(bool disableRedeem = false)
    {
        manualTravel = redeemRunning = redeemReturning = worldOwnsInput = false;
        pendingPurchase = 0;
        CloseOwnedShop(); ownDialogue = ownShop = false;
        if (disableRedeem) autoRedeem = false;
        CancelOwnedNavigation();
        if (disposed && navigation.Owned)
        {
            // No framework ticks remain after unload to stop a path installed by a pending request.
            try
            {
                if (IpcBool("vnavmesh.SimpleMove.PathfindInProgress"))
                    pi.GetIpcSubscriber<object>("vnavmesh.Nav.PathfindCancelAll").InvokeAction();
            }
            catch { }
        }
        if (ownTeleport)
        {
            try { pi.GetIpcSubscriber<object>("Lifestream.Abort").InvokeAction(); } catch { }
            ownTeleport = false;
        }
        worldFault = false; worldMessage = "";
    }
    private void CancelOwnedNavigation()
    {
        navigation.RequestStop();
        if (!navigation.Owned) return;
        try { pi.GetIpcSubscriber<object>("vnavmesh.Path.Stop").InvokeAction(); } catch { }
    }
    private void SetWorldFault(string reason)
    {
        CancelOwnedNavigation();
        worldFault = true;
        worldMessage = reason + "；请点击停止后重试。";
        WriteRecord(new { Kind = "WorldAutomationError", At = DateTimeOffset.Now, Message = reason });
    }
    private IGameObject? FindWorldNpc(uint id)
    {
        var player = objects.LocalPlayer;
        return player == null ? null : objects.Where(o => o.IsValid() && o.IsTargetable && o.ObjectKind == ObjectKind.EventNpc && o.BaseId == id)
            .OrderBy(o => Vector3.DistanceSquared(player.Position, o.Position)).FirstOrDefault();
    }
    private bool IpcBool(string name) => pi.GetIpcSubscriber<bool>(name).InvokeFunc();
    private void ReconcileNavigationStop()
    {
        if (!navigation.StopPending) return;
        try
        {
            if (navigation.UpdateStop(IpcBool("vnavmesh.SimpleMove.PathfindInProgress"), IpcBool("vnavmesh.Path.IsRunning")))
                pi.GetIpcSubscriber<object>("vnavmesh.Path.Stop").InvokeAction();
        }
        catch { /* Keep cancellation pending until the provider returns. */ }
    }
    private unsafe bool ApproachNpc(uint id, Vector3 fallback, long now, bool interact)
    {
        var player = objects.LocalPlayer;
        if (player == null) return false;
        var npc = FindWorldNpc(id);
        var destination = npc?.Position ?? fallback;
        var standing = clientState.TerritoryType == WorldAutomationRules.Plaza ? WorldAutomationRules.StandingPosition(id) : null;
        var arrived = standing is { } stop ? WorldAutomationRules.AtStandingPosition(player.Position, stop, destination)
            : WorldAutomationRules.Arrived(player.Position, destination);
        if (npc != null && arrived)
        {
            if (navigation.Owned) { CancelOwnedNavigation(); return false; }
            if (!interact)
            {
                if (targetManager.Target?.GameObjectId != npc.GameObjectId) targetManager.Target = npc;
                return true;
            }
            if (now < nextWorldAction) return false;
            if (HasWorldDialogues())
            {
                if (!TryAdoptNpcDialogue(id)) worldMessage = "等待关闭当前对话。";
                return false;
            }
            if (targetManager.Target?.GameObjectId != npc.GameObjectId) targetManager.Target = npc;
            if (targetManager.Target?.BaseId != id) return false;
            var system = TargetSystem.Instance();
            if (system == null) return false;
            lastInteractedNpc = id; ownDialogue = true; nextWorldAction = now + 1800;
            system->InteractWithObject((GameObject*)npc.Address, false);
            WriteRecord(new { Kind = "WorldNpcInteraction", At = DateTimeOffset.Now, Npc = id, Position = new[] { npc.Position.X, npc.Position.Y, npc.Position.Z } });
            return false;
        }
        if (navigation.StopPending) return false;
        if (navigation.Owned)
        {
            if (navigationTerritory != clientState.TerritoryType) { CancelOwnedNavigation(); return false; }
            if (now - navigationAt > 120000) { SetWorldFault("寻路超时，未抵达 NPC"); return false; }
            if (IpcBool("vnavmesh.SimpleMove.PathfindInProgress") || IpcBool("vnavmesh.Path.IsRunning")) return false;
            navigation.Arrived();
            if (Vector3.DistanceSquared(player.Position, navigationGoal) > 9) { SetWorldFault("寻路结束但未抵达目标"); return false; }
        }
        if (arrived)
        { worldMessage = "已到预期位置，等待目标 NPC 加载。"; return false; }
        if (!IpcBool("vnavmesh.Nav.IsReady")) { worldMessage = "等待 vnavmesh 地图就绪。"; return false; }
        if (IpcBool("vnavmesh.SimpleMove.PathfindInProgress") || IpcBool("vnavmesh.Path.IsRunning"))
        { worldMessage = "等待其他寻路任务结束。"; return false; }
        // End outside the NPC model. Resolve onto the mesh rather than guessing height from map coordinates.
        var away = player.Position - destination; away.Y = 0;
        if (standing is { } stopPoint) destination = stopPoint;
        else if (away.LengthSquared() > .01f) destination += Vector3.Normalize(away) * 1.8f;
        var point = pi.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPoint").InvokeFunc(destination, 3, 5);
        if (point == null) { SetWorldFault("目标附近没有可用寻路网格"); return false; }
        if (standing is { } fixedStop && Vector2.DistanceSquared(new(point.Value.X, point.Value.Z), new(fixedStop.X, fixedStop.Z)) > .5f * .5f)
        { SetWorldFault("侧边停靠点不在寻路网格上，已停止，避免走入空气墙"); return false; }
        if (pi.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo").InvokeFunc(point.Value, false))
        {
            navigation.Begin(); navigationAt = now; navigationTerritory = clientState.TerritoryType; navigationGoal = point.Value;
            WriteRecord(new { Kind = "NavigationStart", At = DateTimeOffset.Now, Npc = id, Territory = navigationTerritory,
                Destination = new[] { point.Value.X, point.Value.Y, point.Value.Z }, LiveNpc = npc != null });
        }
        return false;
    }
    private unsafe bool EnsurePlaza(long now)
    {
        if (InKeyPlaza)
        {
            ownTeleport = false;
            if (WorldDialogueRules.ClearOnPlazaArrival(lastInteractedNpc)) ownDialogue = false;
            return true;
        }
        if (clientState.TerritoryType != WorldAutomationRules.GoldSaucer)
        {
            if (ownTeleport)
            {
                if (now - teleportAt > 90000) SetWorldFault("传送超时或未能到达金碟游乐场");
                else worldMessage = "等待 Lifestream 传送至金碟游乐场。";
                return false;
            }
            if (IpcBool("Lifestream.IsBusy")) { worldMessage = "等待 Lifestream 当前任务完成。"; return false; }
            if (pi.GetIpcSubscriber<uint, byte, bool>("Lifestream.Teleport").InvokeFunc(WorldAutomationRules.GoldAetheryte, 0))
            { ownTeleport = true; teleportAt = now; worldMessage = "正在传送至金碟游乐场。"; }
            else SetWorldFault("Lifestream 未接受金碟游乐场传送请求，请检查传送点解锁与传送条件");
            return false;
        }
        ownTeleport = false;
        worldMessage = "前往魔光键影向导。";
        if (ownDialogue && lastInteractedNpc == WorldAutomationRules.Guide)
        {
            if (AdvanceOwnedDialogues(now)) return false;
            if (HasWorldDialogues()) return false;
        }
        ApproachNpc(WorldAutomationRules.Guide, WorldAutomationRules.GuidePosition, now, true);
        return false;
    }
    private static unsafe uint ReadKeyPoints()
    {
        var currencies = CurrencyManager.Instance();
        if (currencies != null && currencies->HasItem(WorldAutomationRules.Points)) return currencies->GetItemCount(WorldAutomationRules.Points);
        var inventory = InventoryManager.Instance();
        return inventory == null ? 0 : (uint)Math.Max(0, inventory->GetInventoryItemCount(WorldAutomationRules.Points));
    }
    private void UpdateWorldAutomation(long now)
    {
        ReconcileNavigationStop();
        if (now < nextWorldTick) return;
        worldOwnsInput = WorldBusy || worldFault;
        nextWorldTick = now + 200;
        if (saveLogs && now >= nextWorldRecord && clientState.IsLoggedIn && objects.LocalPlayer is { } recordPlayer)
        {
            nextWorldRecord = now + 1000;
            WriteRecord(new { Kind = "WorldState", At = DateTimeOffset.Now, Territory = clientState.TerritoryType,
                Position = new[] { recordPlayer.Position.X, recordPlayer.Position.Y, recordPlayer.Position.Z }, KeyPoints = ReadKeyPoints(),
                Npcs = objects.Where(o => o.IsValid() && (o.BaseId == EntryNpcBaseId || o.BaseId == WorldAutomationRules.Guide || o.BaseId == WorldAutomationRules.Vendor))
                    .Select(o => new { Id = o.BaseId, Position = new[] { o.Position.X, o.Position.Y, o.Position.Z }, o.IsTargetable }).ToArray() });
        }
        if (worldFault) return;
        if (!WorldBusy && trial == null && !autoRedeem) { worldOwnsInput = false; return; }
        if (!NativeInput.ForegroundIsGame)
        { if (navigation.Owned && !navigation.StopPending) CancelOwnedNavigation(); return; }
        if (!clientState.IsLoggedIn || objects.LocalPlayer == null || ReadNamedAddon("NowLoading") != null) return;
        var game = IsMinigameInProgress();
        var results = ReadNamedAddon("KTGResult") != null;
        var confirm = ReadNamedAddon("ContentsFinderConfirm") != null;
        if (game || results || confirm)
        {
            if (navigation.Owned) CancelOwnedNavigation();
            // Keep a submitted purchase owned until its receipt is reconciled.
            if (pendingPurchase == 0) worldOwnsInput = false;
            return;
        }
        var autoTravel = trial != null && roundLoop != null && RunModePolicy.CanInitiate(activeMode)
            && (roundLoop.Unlimited || roundLoop.Completed < roundLoop.Limit);
        if (autoTravel && (roundLoop!.Unlimited || roundLoop.Completed < roundLoop.Limit)
            && RunModePolicy.PartyError(activeMode, ReadPartyLoopRole()) is { } partyError)
        { worldMessage = partyError; worldOwnsInput = true; return; }
        try
        {
            if (manualTravel || autoTravel)
            {
                worldOwnsInput = true;
                if (!(redeemRunning && pendingPurchase > 0) && GetTravelDependencyError() is { } dependencyError)
                {
                    CancelOwnedNavigation();
                    if (autoTravel && !manualTravel && !redeemRunning && !redeemReturning)
                    {
                        var npc = FindEntryNpc();
                        var player = objects.LocalPlayer;
                        var canInteract = npc != null && player != null && Vector3.DistanceSquared(player.Position, npc.Position) <= 36;
                        var entryOpen = ReadNamedAddon("KTGEnterDialogue") != null;
                        if (DependencyRules.CanRegisterWithoutTravel(InKeyPlaza, entryOpen, canInteract))
                        {
                            if (HasWorldDialogues())
                            {
                                if (ownDialogue && lastInteractedNpc == EntryNpcBaseId || TryAdoptNpcDialogue(EntryNpcBaseId))
                                    AdvanceOwnedDialogues(now);
                                else worldMessage = "等待关闭当前对话。";
                                return;
                            }
                            // The ordinary round loop can interact and register without navigation IPC.
                            worldMessage = "";
                            worldOwnsInput = false;
                            return;
                        }
                        worldMessage = DependencyRules.ManualPlazaMessage;
                        return;
                    }
                    worldMessage = dependencyError;
                    return;
                }
                if (!EnsurePlaza(now)) return;
            }
            if (manualTravel)
            {
                var vendor = manualTravelNpc == WorldAutomationRules.Vendor;
                worldMessage = vendor ? "前往兑换 NPC，抵达后停止寻路。" : "前往报名 NPC，抵达后停止寻路。";
                if (ApproachNpc(manualTravelNpc, vendor ? WorldAutomationRules.VendorPosition : WorldAutomationRules.EntryPosition, now, false))
                { manualTravel = false; worldOwnsInput = false; worldMessage = vendor ? "已抵达兑换 NPC 旁。" : "已抵达报名 NPC 旁，可选择模式并点击开始。"; }
                return;
            }
            if (!redeemRunning && !redeemReturning && !OpeningChests && !HasWorldDialogues()
                && ReadNamedAddon("KTGEnterDialogue") == null
                && WorldAutomationRules.ShouldRedeemWhileQueuing(autoRedeem, trial != null && roundLoop != null, clientState.TerritoryType, ReadKeyPoints(), redeemThreshold)
                && ChestCapacity() > 0 && GetDependencyError(true, false) == null)
            {
                redeemRunning = true; worldOwnsInput = true; ownDialogue = ownShop = false;
                worldMessage = "金碟点超过阈值，前往兑换宝箱。";
                WriteRecord(new { Kind = "RedemptionStart", At = DateTimeOffset.Now, Points = ReadKeyPoints(), Threshold = redeemThreshold });
            }
            if (redeemRunning) { worldOwnsInput = true; UpdateRedemption(now); return; }
            if (redeemReturning)
            {
                worldOwnsInput = true;
                if (GetDependencyError(true, false) is { } returnDependencyError)
                { CancelOwnedNavigation(); worldMessage = returnDependencyError; return; } worldMessage = "兑换结束，返回报名登记员。";
                if (HasWorldDialogues()) { CloseOwnedShop(); return; }
                if (ApproachNpc(EntryNpcBaseId, WorldAutomationRules.EntryPosition, now, false))
                { redeemReturning = false; worldOwnsInput = false; worldMessage = "兑换完成，已返回报名登记员。"; }
                return;
            }
            if (autoTravel)
            {
                if (ReadNamedAddon("KTGEnterDialogue") != null) { worldOwnsInput = false; return; }
                if (HasWorldDialogues())
                {
                    if (ownDialogue && lastInteractedNpc == EntryNpcBaseId) AdvanceOwnedDialogues(now);
                    else worldMessage = "等待关闭当前对话。";
                    return;
                }
                worldMessage = "前往报名登记员。";
                if (ApproachNpc(EntryNpcBaseId, WorldAutomationRules.EntryPosition, now, false)) worldOwnsInput = false;
            }
            else worldOwnsInput = false;
        }
        catch (Exception ex) { log.Error(ex, "MakoMaster world automation paused"); SetWorldFault("传送、寻路或兑换接口不可用：" + ex.Message); }
    }
}
