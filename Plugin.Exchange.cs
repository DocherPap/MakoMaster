using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace MakoMaster;

public sealed partial class Plugin
{
    private static unsafe AtkUnitBase* FindWorldAddon(string name)
    {
        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null) return null;
        var units = &manager->AllLoadedUnitsList;
        for (int i = 0; i < Math.Min((int)units->Count, units->Entries.Length); i++)
        {
            var unit = units->Entries[i].Value;
            if (unit != null && unit->IsVisible && unit->IsReady && unit->NameString == name) return unit;
        }
        return null;
    }
    private bool HasWorldDialogues() => ReadNamedAddon("SelectString") != null || ReadNamedAddon("SelectIconString") != null
        || ReadNamedAddon("SelectYesno") != null || ReadNamedAddon("Talk") != null || ReadNamedAddon("ShopExchangeCurrency") != null;

    private unsafe bool SelectOwnedMenu(string addonName, Func<string, bool> match)
    {
        var unit = FindWorldAddon(addonName);
        if (unit == null) return false;
        // Both selection addons expose the actual popup entries and their live indices.
        var menu = addonName == "SelectString" ? &((AddonSelectString*)unit)->PopupMenu.PopupMenu
            : &((AddonSelectIconString*)unit)->PopupMenu.PopupMenu;
        if (menu->EntryNames == null || menu->EntryCount is < 1 or > 100) return false;
        var index = -1;
        for (int i = 0; i < menu->EntryCount; i++)
        {
            var text = menu->EntryNames[i].ToString();
            if (!match(text)) continue;
            if (index >= 0) return false;
            index = i;
        }
        if (index < 0 || menu->List == null || menu->List->GetItemDisabledState(index)) return false;
        WriteRecord(new { Kind = "WorldMenuSelection", At = DateTimeOffset.Now, Addon = addonName, Index = index, Text = menu->EntryNames[index].ToString() });
        AtkValue argument = default;
        argument.Type = AtkValueType.Int; argument.Int = index;
        return unit->FireCallback(1, &argument, true);
    }
    private bool TryAdoptNpcDialogue(uint npc)
    {
        if (targetManager.Target?.BaseId != npc) return false;
        var speaker = WorldDialogueRules.Speaker(npc);
        var subject = WorldDialogueRules.ConfirmationSubject(npc);
        var talk = ReadNamedAddon("Talk");
        var yes = ReadNamedAddon("SelectYesno");
        var matchingTalk = speaker.Length > 0 && talk?.Nodes.Any(n => n.Visible && n.Text == speaker) == true;
        var matchingConfirm = subject.Length > 0 && yes?.Nodes.Any(n => n.Visible && n.Text.Contains(subject)) == true;
        var matchingMenu = new[] { "SelectString", "SelectIconString" }.Select(ReadNamedAddon)
            .Any(a => a?.Nodes.Any(n => n.Visible && WorldDialogueRules.MenuChoice(npc, n.Text)) == true);
        if (!matchingTalk && !matchingConfirm && !matchingMenu) return false;
        ownDialogue = true; lastInteractedNpc = npc;
        return true;
    }
    private unsafe bool AdvanceOwnedDialogues(long now)
    {
        if (now < nextWorldAction || !WorldDialogueRules.CanHandle(ownDialogue, lastInteractedNpc, targetManager.Target?.BaseId)) return false;
        var selection = new Func<string, bool>(text => WorldDialogueRules.MenuChoice(lastInteractedNpc, text));
        if (SelectOwnedMenu("SelectString", selection) || SelectOwnedMenu("SelectIconString", selection))
        { nextWorldAction = now + 1200; return true; }
        var yes = FindWorldAddon("SelectYesno");
        if (yes != null)
        {
            var snapshot = ReadNamedAddon("SelectYesno");
            var expected = WorldDialogueRules.ConfirmationSubject(lastInteractedNpc);
            if (snapshot?.Nodes.Any(n => n.Visible && n.Text.Contains(expected)) == true
                && expected.Length > 0 && (lastInteractedNpc != WorldAutomationRules.Vendor || pendingPurchase > 0))
            {
                if (ConfirmYesControl(yes)) { nextWorldAction = now + 1200; return true; }
                RecordWorldDialogueWait("等待确认框的“是”按钮就绪");
            }
            return false;
        }
        // An unmatched topmost menu owns the interaction; never click the Talk window behind it.
        if (FindWorldAddon("SelectString") != null || FindWorldAddon("SelectIconString") != null)
        { RecordWorldDialogueWait("等待识别 NPC 选项"); return false; }
        var talk = FindWorldAddon("Talk");
        var speaker = WorldDialogueRules.Speaker(lastInteractedNpc);
        if (talk != null && speaker.Length > 0 && ReadNamedAddon("Talk")?.Nodes.Any(n => n.Visible && n.Text == speaker) == true)
        {
            if (AdvanceTalkControl(talk)) { nextWorldAction = now + 300; return true; }
        }
        if (talk != null) RecordWorldDialogueWait("等待推进 NPC 对话");
        return false;
    }
    private string lastWorldDialogueWait = "";
    private void RecordWorldDialogueWait(string reason)
    {
        var windows = new[] { "Talk", "SelectString", "SelectIconString", "SelectYesno" }
            .Select(ReadNamedAddon).Where(a => a != null)
            .Select(a => new { a!.Name, Text = a.Nodes.Where(n => n.Visible && n.Text.Length > 0).Select(n => n.Text).ToArray() }).ToArray();
        var signature = System.Text.Json.JsonSerializer.Serialize(new { Npc = lastInteractedNpc, reason, Windows = windows });
        worldMessage = reason + "；开启保存日志可记录当前对话和选项。";
        if (signature == lastWorldDialogueWait) return;
        lastWorldDialogueWait = signature;
        WriteRecord(new { Kind = "WorldDialogueWait", At = DateTimeOffset.Now, Npc = lastInteractedNpc, Reason = reason, Windows = windows });
    }
    private unsafe bool SelectOtherCategory(AtkUldManager* manager, int depth = 0)
    {
        if (manager == null || depth > 8 || manager->NodeListCount > 4096) return false;
        for (int i = 0; i < manager->NodeListCount; i++)
        {
            var node = manager->NodeList[i];
            var cn = node == null || !node->IsVisible() ? null : node->GetAsAtkComponentNode();
            if (cn == null || cn->Component == null) continue;
            if (cn->Component->GetComponentType() == ComponentType.DropDownList)
            {
                var dropdown = (AtkComponentDropDownList*)cn->Component;
                var list = dropdown->List;
                if (list == null || list->GetItemCount() is < 1 or > 100) continue;
                for (int j = 0; j < list->GetItemCount(); j++)
                    if (list->GetItemLabel(j).ToString().Trim() == "其他" && dropdown->GetSelectedItemIndex() != j)
                    { dropdown->SelectItem(j); return true; }
            }
            if (SelectOtherCategory(&cn->Component->UldManager, depth + 1)) return true;
        }
        return false;
    }
    private unsafe int ChestCapacity()
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null) return 0;
        int capacity = 0;
        foreach (var bag in chestBags)
        {
            var container = inventory->GetInventoryContainer(bag);
            if (container == null || !container->IsLoaded) return 0;
            for (int i = 0; i < Math.Clamp(container->Size, 0, 512); i++)
            {
                var slot = inventory->GetInventorySlot(bag, i);
                if (slot == null) return 0;
                if (slot->ItemId == 0 || slot->Quantity == 0) capacity += 999;
                else if (slot->GetBaseItemId() == WorldAutomationRules.Chest) capacity += Math.Max(0, 999 - (int)slot->Quantity);
            }
        }
        return capacity;
    }
    private unsafe void CloseOwnedShop()
    {
        if ((!ownShop && !ownDialogue) || lastInteractedNpc != WorldAutomationRules.Vendor) return;
        // Closing a shop may reopen its parent topic menu on the following frame.
        var closed = false;
        foreach (var name in new[] { "ShopExchangeCurrency", "SelectString", "SelectIconString", "Talk" })
        {
            var unit = FindWorldAddon(name);
            if (unit == null) continue;
            unit->FireCloseCallback();
            closed = true;
        }
        if (!closed) ownShop = ownDialogue = false;
    }
    private void EndRedemption(string message)
    {
        redeemRunning = false; redeemReturning = true;
        CloseOwnedShop(); worldMessage = message;
    }
    private unsafe void UpdateRedemption(long now)
    {
        if (!InKeyPlaza) { SetWorldFault("兑换过程中离开了键斗广场"); return; }
        if (redeemThreshold < 0) { SetWorldFault("兑换阈值不能小于 0"); return; }
        var stock = ReadChestInventory(WorldAutomationRules.Chest);
        if (!stock.Loaded) { worldMessage = "等待背包数据加载。"; return; }
        var points = ReadKeyPoints();
        if (pendingPurchase > 0)
        {
            if (WorldAutomationRules.Receipt(purchaseBeforePoints, purchaseBeforeCount, points, stock.Count, pendingPurchase))
            {
                WriteRecord(new { Kind = "ChestExchangeConfirmed", At = DateTimeOffset.Now, Quantity = pendingPurchase, RemainingPoints = points });
                pendingPurchase = 0; nextWorldAction = now + 1000;
            }
            else
            {
                if (now - purchaseAt > 15000) { SetWorldFault("兑换未收到一致的点数与宝箱变化，已暂停，未重复购买"); return; }
                AdvanceOwnedDialogues(now);
                worldMessage = "等待宝箱兑换结果。";
                return;
            }
        }
        if (GetDependencyError(true, false) is { } dependencyError)
        { CancelOwnedNavigation(); worldMessage = dependencyError; return; }
        var quantity = WorldAutomationRules.PurchaseQuantity(points, ChestCapacity());
        if (quantity == 0) { EndRedemption(points < WorldAutomationRules.ChestPrice ? "点数已尽量兑换完毕。" : "背包空间不足，结束兑换。"); return; }
        if (now < nextWorldAction) return;
        if (targetManager.Target?.BaseId != WorldAutomationRules.Vendor && ownDialogue)
        { SetWorldFault("兑换目标 NPC 已改变"); return; }
        var shop = FindWorldAddon("ShopExchangeCurrency");
        if (shop == null)
        {
            if (ownDialogue && lastInteractedNpc == WorldAutomationRules.Vendor)
            {
                if (AdvanceOwnedDialogues(now)) return;
                if (HasWorldDialogues()) { worldMessage = "等待识别兑换菜单中的“其他”。"; return; }
            }
            worldMessage = "前往金碟点兑换员。";
            ApproachNpc(WorldAutomationRules.Vendor, WorldAutomationRules.VendorPosition, now, true);
            return;
        }
        if (!ownDialogue || lastInteractedNpc != WorldAutomationRules.Vendor || targetManager.Target?.BaseId != WorldAutomationRules.Vendor)
        { SetWorldFault("兑换窗口不属于当前金碟点兑换员"); return; }
        ownShop = true;
        var agent = AgentShop.Instance();
        if (agent == null || !agent->IsAgentActive() || agent->ItemReceive == null || agent->ItemCost == null
            || agent->ItemReceiveCount is < 1 or > 1000 || agent->ItemCostCount < agent->ItemReceiveCount * 3)
        { worldMessage = "等待兑换商店数据加载。"; return; }
        int row = -1;
        for (int i = 0; i < agent->ItemReceiveCount; i++)
            if (agent->ItemReceive[i].ItemId == WorldAutomationRules.Chest)
            { if (row >= 0) { SetWorldFault("商店中存在重复宝箱行"); return; } row = i; }
        if (row < 0)
        {
            if (SelectOtherCategory(&shop->UldManager)) { nextWorldAction = now + 1200; return; }
            worldMessage = "等待兑换窗口显示“其他”分类与魔光键影宝箱。"; return;
        }
        var offer = agent->ItemReceive[row];
        var cost = agent->ItemCost[row * 3];
        WriteRecord(new { Kind = "LiveChestExchangeOffer", At = DateTimeOffset.Now, Row = row,
            ReceiveId = offer.ItemId, ReceiveQuantity = offer.ItemCount, CurrencyId = cost.ItemId, Price = cost.ItemCount,
            ExtraCost1 = agent->ItemCost[row * 3 + 1].ItemCount, ExtraCost2 = agent->ItemCost[row * 3 + 2].ItemCount });
        if (offer.ItemCount != 1 || cost.ItemId != WorldAutomationRules.Points || cost.ItemCount != WorldAutomationRules.ChestPrice
            || agent->ItemCost[row * 3 + 1].ItemCount != 0 || agent->ItemCost[row * 3 + 2].ItemCount != 0)
        { SetWorldFault("宝箱实时价格或兑换货币与已核实数据不一致"); return; }
        // Exact item/currency/price validated above; ShopExchangeCurrency accepts (0, row, quantity, 0).
        AtkValue* values = stackalloc AtkValue[4];
        for (int i = 0; i < 4; i++) { values[i] = default; values[i].Type = AtkValueType.Int; }
        values[1].Int = row; values[2].Int = quantity;
        purchaseBeforeCount = stock.Count; purchaseBeforePoints = points; pendingPurchase = quantity; purchaseAt = now;
        nextWorldAction = now + 1000;
        WriteRecord(new { Kind = "ChestExchangeRequested", At = DateTimeOffset.Now, Row = row, Quantity = quantity, Points = points, Price = cost.ItemCount });
        shop->FireCallback(4, values, true);
    }
}
