namespace MakoMaster;

// Counts observed single-item consumption, never merely a submitted use request.
internal sealed class ChestOpeningSession
{
    internal int Limit { get; }
    internal int Opened { get; private set; }
    internal int Remaining { get; private set; }
    internal bool Running { get; private set; } = true;
    internal string Status { get; private set; } = "等待背包就绪";
    private bool pending;
    private int beforeUse;
    private long pendingAt, nextUse;
    internal ChestOpeningSession(int limit)
    {
        if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit), "开启数量不能小于 0");
        Limit = limit;
    }
    internal bool Tick(bool loaded, int count, bool canUse, long now)
    {
        if (!Running) return false;
        if (!loaded) { Status = "等待背包载入"; return false; }
        Remaining = Math.Max(0, count);
        if (pending)
        {
            var consumed = beforeUse - count;
            if (consumed == 1)
            {
                pending = false; Opened++;
                if (Limit > 0 && Opened >= Limit) { Stop($"已开启 {Opened} 个，达到指定数量"); return false; }
                if (count == 0) { Stop($"已开启 {Opened} 个，宝箱已用完"); return false; }
                nextUse = now + 1000; Status = $"已开启 {Opened} 个，准备下一个";
                return false;
            }
            if (consumed > 1) { Stop("宝箱数量出现额外变动，已停止，请检查背包"); return false; }
            if (now - pendingAt >= 8000) { Stop("未确认宝箱减少，已停止，避免重复使用"); return false; }
            Status = "等待宝箱使用结果"; return false;
        }
        if (count <= 0) { Stop($"已开启 {Opened} 个，背包中没有魔光键影宝箱"); return false; }
        if (Limit > 0 && Opened >= Limit) { Stop($"已开启 {Opened} 个，达到指定数量"); return false; }
        if (now < nextUse) return false;
        if (!canUse) { Status = "等待可使用宝箱（请回到游戏并结束当前动作）"; return false; }
        beforeUse = count; Status = "准备开启魔光键影宝箱";
        return true;
    }
    internal void ActionSent(long now) { pending = true; pendingAt = now; Status = "等待宝箱使用结果"; }
    internal void Stop(string reason) { Running = false; Status = reason; }
}
