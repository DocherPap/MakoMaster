using MakoMaster;

internal static class InputLanguageTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static void Run()
    {
        nint english = 0x04090409, chinese = 0x08040804, traditional = 0x04040404;
        Check(InputLanguageGuard.IsChinese(chinese) && InputLanguageGuard.IsChinese(traditional) && !InputLanguageGuard.IsChinese(english), "Chinese locale detection");
        Check(InputLanguageGuard.ChooseChineseLayout([traditional, english, chinese]) == chinese, "simplified Chinese layout preference");
        var platform = new Fake { State = new(true, english, false, false), Layouts = [english, chinese] };
        var guard = new InputLanguageGuard(platform);
        Check(!guard.Tick(0) && platform.LayoutRequests.SequenceEqual(new[] { chinese }), "non-Chinese startup did not request switching");
        Check(!guard.Tick(250) && platform.LayoutRequests.Count == 1, "layout requests ignored backoff");
        platform.State = new(true, chinese, false, false);
        Check(!guard.Tick(1500) && platform.ModeRequests == 1, "Chinese layout in English mode was accepted without switching");
        platform.State = new(true, chinese, true, true);
        Check(guard.Tick(1750), "confirmed Chinese input was not allowed");
        platform.State = new(true, english, false, false);
        Check(!guard.Tick(2000), "user language change while running was not detected");
        guard.Reset();
        platform.State = new(false, english, false, false);
        var requests = platform.LayoutRequests.Count;
        Check(!guard.Tick(0) && platform.LayoutRequests.Count == requests, "background application language was changed");
        var absent = new Fake { State = new(true, english, false, false), Layouts = [english] };
        Check(!new InputLanguageGuard(absent).Tick(0) && absent.LayoutRequests.Count == 0, "missing Chinese layout was guessed or installed");
        var modern = new Fake { State = new(true, chinese, null, null), Layouts = [chinese] };
        var modernGuard = new InputLanguageGuard(modern);
        Check(!modernGuard.Tick(0) && !modernGuard.Tick(1000) && !modernGuard.Tick(2000), "unknown mode was immediately claimed verified");
        Check(modernGuard.Tick(2250) && modern.ModeRequests == 3 && modernGuard.Status.Contains("未提供"), "unknown TSF mode should attempt switching then disclose uncertainty");
        var latin = new Fake { State = new(true, chinese, false, true), Layouts = [chinese] };
        var latinGuard = new InputLanguageGuard(latin);
        for (long time=0;time<5000;time+=1000) Check(!latinGuard.Tick(time), "known English mode was incorrectly accepted");
        guard.Reset(); Check(!guard.Ready, "stop/restart reset retained an armed input guard");
        Console.WriteLine("PASS: Chinese layout and mode detection; game-only switching; request backoff; missing-layout guard; TSF uncertainty; stop/restart reset.");
    }
    private sealed class Fake : IInputLanguagePlatform
    {
        internal InputLanguageState State = new(false,0,null,null);
        internal nint[] Layouts = [];
        internal readonly List<nint> LayoutRequests = [];
        internal int ModeRequests;
        public InputLanguageState Read() => State;
        public IReadOnlyList<nint> InstalledLayouts() => Layouts;
        public void RequestLayout(nint layout) => LayoutRequests.Add(layout);
        public void RequestChineseMode() => ModeRequests++;
    }
}
