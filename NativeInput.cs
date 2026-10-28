using System.Runtime.InteropServices;

namespace MakoMaster;

// Windows x64 INPUT layout; only pinyin letters, candidate digits and Enter.
// Every key is released on a later framework frame; unload also releases it.
internal sealed class NativeInput
{
    private ushort heldScan;
    private long releaseAt;
    private bool heldExtended;
    internal bool Holding => heldScan != 0;
    internal static bool ForegroundIsGame
    {
        get { GetWindowThreadProcessId(GetForegroundWindow(), out var pid); return pid == Environment.ProcessId; }
    }
    internal static bool ModifiersPressed => Down(0x10) || Down(0x11) || Down(0x12) || Down(0x5B) || Down(0x5C);
    internal static bool CapsLockOn => (GetKeyState(0x14) & 1) != 0;
    internal static bool TryGetClientSize(out int width, out int height)
    {
        width = height = 0;
        if (!ForegroundIsGame || !GetClientRect(GetForegroundWindow(), out var rect)) return false;
        width = rect.Right - rect.Left; height = rect.Bottom - rect.Top;
        return width > 0 && height > 0;
    }
    internal static string InputBlockReason
    {
        get
        {
            var keys = new List<string>();
            if (Down(0x10)) keys.Add("Shift");
            if (Down(0x11)) keys.Add("Ctrl");
            if (Down(0x12)) keys.Add("Alt");
            if (Down(0x5B) || Down(0x5C)) keys.Add("Win");
            if (CapsLockOn) keys.Add("CapsLock");
            return string.Join(" + ", keys);
        }
    }
    private static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    internal void ReleaseDue(long now) { if (Holding && now >= releaseAt) Release(); }
    internal void Press(char key, long now)
    {
        if (Holding) throw new InvalidOperationException("Previous key is still held");
        if (!ForegroundIsGame || ModifiersPressed || CapsLockOn)
            throw new InvalidOperationException("游戏失去焦点，或修饰键/CapsLock 已按下");
        var vk = key == '\n' ? 0x0D : key == '\f' ? 0x22 : key == '\b' ? 0x08
            : key is >= 'a' and <= 'z' ? char.ToUpperInvariant(key)
            : key is >= '1' and <= '9' ? key : throw new ArgumentOutOfRangeException(nameof(key));
        if (Down(vk)) throw new InvalidOperationException("需要发送的按键正被手动按住");
        var scan = (ushort)MapVirtualKeyW((uint)vk, 0);
        if (scan == 0) throw new InvalidOperationException("无法映射键盘扫描码");
        heldScan = scan;
        heldExtended = key == '\f';
        try { Send(scan, false, heldExtended); releaseAt = now + 1; }
        catch { Release(); throw; }
    }
    internal void Release()
    {
        var scan = heldScan;
        if (scan == 0) return;
        Send(scan, true, heldExtended);
        heldScan = 0;
    }
    private static void Send(ushort scan, bool up, bool extended)
    {
        var events = new[] { new Input { Type = 1, Scan = scan, Flags = 0x0008u | (up ? 0x0002u : 0) | (extended ? 1u : 0u) } };
        if (SendInput(1, events, Marshal.SizeOf<Input>()) != 1)
            throw new InvalidOperationException("SendInput 未成功，Windows 错误码：" + Marshal.GetLastWin32Error());
    }
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ushort VirtualKey;
        [FieldOffset(10)] public ushort Scan;
        [FieldOffset(12)] public uint Flags;
        [FieldOffset(16)] public uint Time;
        [FieldOffset(24)] public nuint ExtraInfo;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] events, int size);
    [DllImport("user32.dll")] private static extern uint MapVirtualKeyW(uint code, uint mapType);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out int pid);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hwnd, out Rect rect);
}
