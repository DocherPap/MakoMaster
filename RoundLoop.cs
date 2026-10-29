namespace MakoMaster;

internal enum PartyLoopRole { Disabled, Waiting, Leader, Member }
internal static class PartyLoopPolicy
{
    internal static PartyLoopRole ResolveCrossRealm(int count, bool alliance, bool? localIsLeader)
    {
        if (count != 4 || alliance) return PartyLoopRole.Disabled;
        return localIsLeader switch { true => PartyLoopRole.Leader, false => PartyLoopRole.Member, null => PartyLoopRole.Waiting };
    }
    internal static PartyLoopRole Resolve(int count, bool alliance, uint? leaderId, uint? playerId)
    {
        if (count != 4 || alliance) return PartyLoopRole.Disabled;
        if (leaderId is null or 0 or uint.MaxValue || playerId is null or 0 or uint.MaxValue) return PartyLoopRole.Waiting;
        return leaderId == playerId ? PartyLoopRole.Leader : PartyLoopRole.Member;
    }
}

internal enum RoundAction { None, Interact, Apply, Accept, Exit, Finish }
internal sealed record RoundView(bool Home, bool Loading, bool Game, bool Entry, bool Confirm, bool Results);

// A known entry NPC or registration window is stronger evidence than lingering game UI.
internal sealed record RoundScene(uint Territory, bool Loading, bool GameUi, bool Entry, bool Confirm, bool Results, bool EntryNpc)
{
    internal bool HasEntryEvidence => !Loading && (EntryNpc || Entry);
    internal RoundView ToView(uint? homeTerritory)
    {
        var game = GameUi && !HasEntryEvidence;
        var home = !Loading && (HasEntryEvidence || homeTerritory == Territory && !game);
        return new(home, Loading, game, Entry, Confirm, Results);
    }
}
// A result window completes a round; waves, empty words and loading screens do not.
internal sealed class RoundLoop(int limit, bool exitExistingResult = false)
{
    internal int Limit { get; } = limit is >= 1 and <= 9999 ? limit : throw new ArgumentOutOfRangeException(nameof(limit));
    internal int Completed { get; private set; }
    internal string Status { get; private set; } = "等待自动选择入场 NPC";
    private bool played, leaving, registered;
    private long nextAction, homeSince;
    private string sentScreen = "";

    internal RoundAction Tick(RoundView view, long now, bool canInitiate = true)
    {
        if (view.Game && !leaving) played = true;
        if (view.Results)
        {
            if (!played)
            {
                Status = exitExistingResult ? "退出上一局结算，准备开始新的一局" : "等待新一局，当前结算不计次";
                return exitExistingResult && now >= nextAction ? RoundAction.Exit : RoundAction.None;
            }
            if (!leaving) { Completed++; leaving = true; }
            Status = $"已完成 {Completed}/{Limit} 局，退出结算";
            return now >= nextAction ? RoundAction.Exit : RoundAction.None;
        }
        if (view.Loading) { homeSince = 0; Status = "等待场景加载"; return RoundAction.None; }
        if (leaving)
        {
            if (!view.Home || view.Game) { homeSince = 0; Status = "等待返回 NPC 所在区域"; return RoundAction.None; }
            if (homeSince == 0) homeSince = now;
            if (now - homeSince < 1500) return RoundAction.None;
            if (Completed >= Limit) { Status = $"循环完成：{Completed}/{Limit} 局"; return RoundAction.Finish; }
            played = leaving = registered = false; sentScreen = ""; homeSince = 0;
        }
        if (view.Game) { sentScreen = ""; Status = $"自动打字：第 {Completed + 1}/{Limit} 局"; return RoundAction.None; }
        if (!canInitiate && !view.Confirm)
        {
            Status = "队员模式：等待队长开始游戏";
            return RoundAction.None;
        }
        var screen = view.Confirm ? "confirm" : view.Entry ? "entry" : "home";
        // Never submit a still-visible registration / departure button twice.
        if (screen != sentScreen && now >= nextAction)
        {
            if (view.Confirm) { Status = "确认魔光键影出发"; return RoundAction.Accept; }
            if (view.Entry && !played) { Status = "申请参加魔光键影"; return RoundAction.Apply; }
            if (view.Home && !played && !registered) { Status = "自动选择入场 NPC 并交互"; return RoundAction.Interact; }
        }
        Status = played ? "等待下一关或成绩窗口" : "等待申请、匹配或出发界面";
        return RoundAction.None;
    }
    internal void ActionSent(RoundAction action, long now)
    {
        nextAction = now + 2000;
        if (action is RoundAction.Apply or RoundAction.Accept) registered = true;
        sentScreen = action switch { RoundAction.Apply => "entry", RoundAction.Accept => "confirm", RoundAction.Interact => "home", _ => sentScreen };
    }
}
