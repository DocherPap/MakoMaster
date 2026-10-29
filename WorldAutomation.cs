using System.Numerics;

namespace MakoMaster;

internal static class WorldAutomationRules
{
    internal const uint GoldSaucer = 144, Plaza = 1360, GoldAetheryte = 62;
    internal const uint Guide = 1058759, Vendor = 1058753, Chest = 52450, Points = 52322;
    internal const uint Shop = 1771021, ChestPrice = 910;
    // Verified from the installed game's planevent.lgb and Level sheet; live objects take precedence.
    internal static readonly Vector3 GuidePosition = new(-9.9009f, 0, 11.0887f);
    internal static readonly Vector3 EntryPosition = new(-12.099f, .332855f, -31.2828f);
    internal static readonly Vector3 VendorPosition = new(8.20737f, 0, -29.1535f);
    // User-recorded reachable standing positions beside the counters, outside their collision walls.
    internal static readonly Vector3 EntryStandingPosition = new(-9.322489f, .025749326f, -30.488832f);
    internal static readonly Vector3 VendorStandingPosition = new(6.0622134f, 0, -28.247383f);
    internal static Vector3? StandingPosition(uint npc) => npc switch
    {
        WorldDialogueRules.RegistrationNpc => EntryStandingPosition,
        Vendor => VendorStandingPosition,
        _ => null,
    };
    internal static bool AtStandingPosition(Vector3 player, Vector3 standing, Vector3 npc)
        => Vector2.DistanceSquared(new(player.X, player.Z), new(standing.X, standing.Z)) <= .65f * .65f
            && Math.Abs(player.Y - standing.Y) <= 1.5f && Vector3.DistanceSquared(player, npc) <= 36;
    internal static bool ShouldRedeemWhileQueuing(bool enabled, bool queuing, uint territory, uint points, int threshold)
        => queuing && ShouldRedeem(enabled, territory, points, threshold);
    internal static bool ShouldRedeem(bool enabled, uint territory, uint points, int threshold)
        => enabled && territory == Plaza && threshold >= 0 && points > threshold && points >= ChestPrice;
    internal static int PurchaseQuantity(uint points, int capacity) => Math.Min(99, Math.Min((int)(points / ChestPrice), Math.Max(0, capacity)));
    internal static bool Receipt(uint beforePoints, int beforeItems, uint nowPoints, int nowItems, int quantity)
        => quantity > 0 && beforePoints >= (uint)quantity * ChestPrice
            && beforePoints - (uint)quantity * ChestPrice == nowPoints && nowItems - beforeItems == quantity;
    internal static bool Arrived(Vector3 player, Vector3 destination)
        => Vector3.DistanceSquared(player, destination) <= 2.8f * 2.8f;
}

internal sealed class NavigationRequestGate
{
    internal bool Owned { get; private set; }
    internal bool StopPending { get; private set; }
    internal void Begin() { Owned = true; StopPending = false; }
    internal void RequestStop() { if (Owned) StopPending = true; }
    // A vnavmesh request may install its path after Stop was pressed. Keep stopping until it settles.
    internal bool UpdateStop(bool calculating, bool moving)
    {
        if (!Owned || !StopPending) return false;
        if (!calculating && !moving) { Owned = StopPending = false; return false; }
        return moving;
    }
    internal void Arrived() { Owned = StopPending = false; }
}

internal static class WorldDialogueRules
{
    internal const uint RegistrationNpc = 1058751;
    internal static string Speaker(uint npc) => npc switch
    {
        WorldAutomationRules.Guide => "魔光键影向导",
        RegistrationNpc => "报名登记员",
        WorldAutomationRules.Vendor => "金碟点兑换员",
        _ => "",
    };

    internal static bool CanHandle(bool owned, uint npc, uint? target)
        => owned && npc is WorldAutomationRules.Guide or WorldAutomationRules.Vendor or RegistrationNpc
            && (target == null || target == npc);
    internal static bool CanConfirm(bool owned, uint npc, uint? target, string question, int pendingPurchase)
    {
        if (!CanHandle(owned, npc, target) || npc == WorldAutomationRules.Vendor && pendingPurchase <= 0) return false;
        var expected = ConfirmationSubject(npc);
        return expected.Length > 0 && question.Contains(expected, StringComparison.Ordinal);
    }
    internal static bool ClearOnPlazaArrival(uint npc) => npc == WorldAutomationRules.Guide;
    internal static bool MenuChoice(uint npc, string text)
    {
        var value = WordMatching.Normalize(text);
        if (value.Contains("离开") || value.Contains("取消") || value.Contains("不前往") || value.Contains("不参加") || value.Contains("说明")) return false;
        return npc switch
        {
            WorldAutomationRules.Guide => value.Contains("键斗广场") && (value.Contains("前往") || value.Contains("进入")),
            WorldAutomationRules.Vendor => value == "其他",
            RegistrationNpc => value is "报名" or "参加" or "申请参加"
                || value.Contains("魔光键影") && (value.Contains("报名") || value.Contains("参加")),
            _ => false,
        };
    }
    internal static string ConfirmationSubject(uint npc) => npc switch
    {
        WorldAutomationRules.Guide => "键斗广场",
        RegistrationNpc => "魔光键影",
        WorldAutomationRules.Vendor => "魔光键影宝箱",
        _ => "",
    };
}
