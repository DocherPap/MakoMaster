namespace MakoMaster;

internal sealed record InputLanguageState(bool GameForeground, nint Layout, bool? NativeMode, bool? Open);
internal interface IInputLanguagePlatform
{
    InputLanguageState Read();
    IReadOnlyList<nint> InstalledLayouts();
    void RequestLayout(nint layout);
    void RequestChineseMode();
}

internal sealed class InputLanguageGuard(IInputLanguagePlatform platform)
{
    private long nextCheck, nextChange;
    private int modeAttempts;
    internal bool Ready { get; private set; }
    internal string Status { get; private set; } = "检查中文输入法";
    internal static bool IsChinese(nint layout) => ((long)layout & 0x3FF) == 0x04;
    internal static nint ChooseChineseLayout(IEnumerable<nint> layouts)
        => layouts.Where(IsChinese).OrderByDescending(l => ((long)l & 0xFFFF) == 0x0804).FirstOrDefault();
    internal void Reset() { nextCheck = nextChange = 0; modeAttempts = 0; Ready = false; Status = "检查中文输入法"; }
    internal bool Tick(long now)
    {
        if (now < nextCheck) return Ready;
        nextCheck = now + 250;
        var state = platform.Read();
        Ready = false;
        if (!state.GameForeground) { Status = "等待游戏窗口获得焦点"; return false; }
        if (!IsChinese(state.Layout))
        {
            modeAttempts = 0;
            var target = ChooseChineseLayout(platform.InstalledLayouts());
            if (target == 0) { Status = "未找到已安装的中文输入法，请先安装或启用中文输入法"; return false; }
            Status = "正在切换到中文输入法";
            if (now >= nextChange) { platform.RequestLayout(target); nextChange = now + 1500; }
            return false;
        }
        if (state.NativeMode == true && state.Open != false)
        { Ready = true; Status = "中文输入法已就绪"; modeAttempts = 0; return true; }
        if (state.NativeMode == null && state.Open != false && modeAttempts >= 3)
        { Ready = true; Status = "已选中中文输入法；输入法未提供中英文模式状态，请确认中文模式"; return true; }
        Status = "正在尝试开启中文输入模式";
        if (now >= nextChange) { platform.RequestChineseMode(); modeAttempts++; nextChange = now + 1000; }
        return false;
    }
}
