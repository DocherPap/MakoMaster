using System.Runtime.InteropServices;
namespace MakoMaster;

// Operates only on the game's foreground window; no global hotkeys or foreground stealing.
internal sealed class WindowsInputLanguage : IInputLanguagePlatform
{
    private static nint Window()
    {
        var window = GetForegroundWindow();
        GetWindowThreadProcessId(window, out var process);
        return process == Environment.ProcessId ? window : 0;
    }
    public InputLanguageState Read()
    {
        var window = Window();
        if (window == 0) return new(false, 0, null, null);
        var thread = GetWindowThreadProcessId(window, out _);
        var layout = GetKeyboardLayout(thread);
        var context = ImmGetContext(window);
        if (context == 0) return new(true, layout, null, null);
        try
        {
            var known = ImmGetConversionStatus(context, out var conversion, out _);
            return new(true, layout, known ? (conversion & 1u) != 0 : null, known ? ImmGetOpenStatus(context) : null);
        }
        finally { ImmReleaseContext(window, context); }
    }
    public IReadOnlyList<nint> InstalledLayouts()
    {
        var count = Math.Clamp(GetKeyboardLayoutList(0, null), 0, 128);
        if (count == 0) return [];
        var layouts = new nint[count];
        var actual = GetKeyboardLayoutList(count, layouts);
        return layouts.Take(Math.Clamp(actual, 0, layouts.Length)).ToArray();
    }
    public void RequestLayout(nint layout)
    {
        var window = Window();
        if (window == 0 || !InputLanguageGuard.IsChinese(layout)) return;
        if (!PostMessageW(window, 0x0050, 0, layout)) throw new InvalidOperationException("无法向游戏请求切换中文输入法");
    }
    public void RequestChineseMode()
    {
        var window = Window();
        if (window == 0) return;
        var context = ImmGetContext(window);
        if (context == 0) return;
        try
        {
            ImmSetOpenStatus(context, true);
            if (ImmGetConversionStatus(context, out var conversion, out var sentence))
                ImmSetConversionStatus(context, (conversion | 1u) & ~8u, sentence);
        }
        finally { ImmReleaseContext(window, context); }
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] private static extern int GetKeyboardLayoutList(int count, [Out] nint[]? layouts);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("imm32.dll")] private static extern nint ImmGetContext(nint window);
    [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ImmReleaseContext(nint window, nint context);
    [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ImmGetOpenStatus(nint context);
    [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ImmSetOpenStatus(nint context, [MarshalAs(UnmanagedType.Bool)] bool open);
    [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ImmGetConversionStatus(nint context, out uint conversion, out uint sentence);
    [DllImport("imm32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ImmSetConversionStatus(nint context, uint conversion, uint sentence);
}
