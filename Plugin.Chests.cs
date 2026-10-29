using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace MakoMaster;
public sealed partial class Plugin
{
    private readonly IDataManager dataManager;
    private int chestCount;
    private uint chestItemId;
    private ChestOpeningSession? chestOpening;
    private string chestMessage = "";
    private long nextChestCheck;
    private static readonly InventoryType[] chestBags = [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];
    private sealed record ChestInventory(bool Loaded, int Count, InventoryType Bag, int Slot);
    private bool OpeningChests => chestOpening?.Running == true;

    private void DrawChestControls()
    {
        if (OpeningChests)
            ImGui.TextUnformatted($"已开启 {chestOpening!.Opened} 个 · 背包剩余 {chestOpening.Remaining} 个");
        var message = chestOpening?.Status ?? chestMessage;
        if (message.Length > 0) ImGui.TextWrapped(message);
    }
    private void StartChestOpening()
    {
        if (WorldBusy || worldFault) { chestMessage = "请先停止传送、寻路或兑换操作。"; return; }
        if (trial != null) { chestMessage = "请先停止自动打字或循环，再开启宝箱。"; return; }
        if (OpeningChests) return;
        if (chestCount < 0) { chestMessage = "开启数量不能小于 0；填 0 表示一直开。"; return; }
        if (chestItemId == 0)
        {
            var sheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            var matches = sheet.Where(item => item.Name.ExtractText() == "魔光键影宝箱").Select(item => item.RowId).Take(2).ToArray();
            if (matches.Length != 1) { chestMessage = "游戏物品数据中未能唯一识别魔光键影宝箱，未使用任何物品。"; return; }
            chestItemId = matches[0];
        }
        chestOpening = new(chestCount); nextChestCheck = 0; chestMessage = "";
        WriteRecord(new { Kind = "ChestOpeningStart", At = DateTimeOffset.Now, ItemId = chestItemId, Limit = chestCount });
    }
    private void StopChestOpening(string reason)
    {
        if (!OpeningChests) return;
        chestOpening!.Stop(reason);
        WriteRecord(new { Kind = "ChestOpeningEnd", At = DateTimeOffset.Now, chestOpening.Opened, Reason = reason });
    }
    private static unsafe ChestInventory ReadChestInventory(uint itemId)
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null) return new(false,0,default,0);
        var total = 0; var firstBag = default(InventoryType); var firstSlot = 0; var found = false;
        foreach (var bag in chestBags)
        {
            var container = inventory->GetInventoryContainer(bag);
            if (container == null || !container->IsLoaded) return new(false,0,default,0);
            for (var index = 0; index < Math.Clamp(container->Size,0,512); index++)
            {
                var slot = container->GetInventorySlot(index);
                if (slot == null || slot->GetBaseItemId() != itemId || slot->Quantity <= 0) continue;
                total = checked(total + slot->Quantity);
                if (!found) { firstBag = bag; firstSlot = index; found = true; }
            }
        }
        return new(true,total,firstBag,firstSlot);
    }
    private unsafe void UpdateChestOpening(long now)
    {
        if (!OpeningChests || now < nextChestCheck) return;
        nextChestCheck = now + 250;
        try
        {
            var stock = clientState.IsLoggedIn && objects.LocalPlayer != null ? ReadChestInventory(chestItemId) : new(false,0,default,0);
            var actions = ActionManager.Instance();
            var canUse = trial == null && NativeInput.ForegroundIsGame && actions != null && stock.Loaded && stock.Count > 0
                && actions->GetActionStatus(ActionType.Item,chestItemId) == 0;
            var before = chestOpening!.Opened;
            if (chestOpening.Tick(stock.Loaded,stock.Count,canUse,now))
            {
                var inventory = InventoryManager.Instance();
                var slot = inventory == null ? null : inventory->GetInventorySlot(stock.Bag,stock.Slot);
                var agent = AgentInventoryContext.Instance();
                if (slot == null || slot->GetBaseItemId() != chestItemId || slot->Quantity <= 0 || agent == null) return;
                var result = agent->UseItem(chestItemId,stock.Bag,(uint)stock.Slot);
                chestOpening.ActionSent(now);
                WriteRecord(new { Kind = "ChestUseRequested", At = DateTimeOffset.Now, ItemId = chestItemId, Bag = stock.Bag.ToString(), stock.Slot, Result = result });
            }
            if (chestOpening.Opened != before)
                WriteRecord(new { Kind = "ChestOpened", At = DateTimeOffset.Now, chestOpening.Opened, chestOpening.Remaining });
        }
        catch (Exception ex)
        {
            StopChestOpening("开箱异常，已停止："+ex.Message);
            log.Error(ex,"MakoMaster chest opening stopped");
        }
    }
}
