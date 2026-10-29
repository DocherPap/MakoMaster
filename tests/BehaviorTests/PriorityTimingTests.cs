using MakoMaster;
internal static class PriorityTimingTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static readonly Dictionary<string,DictionaryEntry> Bank = new()
    {
        ["近"] = new("近",["jin"],"jin","jin"),
        ["计步"] = new("计步",["ji","bu"],"jibu","ji'bu"),
        ["计数"] = new("计数",["ji","shu"],"jishu","ji'shu"),
        ["步行"] = new("步行",["bu","xing"],"buxing","bu'xing")
    };
    private static TrialView View(string buffer, IReadOnlyList<TrialWord> words, Dictionary<int,string>? candidates=null)
        => new(true,buffer,words,candidates ?? new());
    private static TrialMachine Machine(int wordPause=400) => new(Bank,0,new Random(1),true,new TimingSettings(100,100,wordPause,wordPause));
    internal static void Run()
    {
        var normal=new TrialWord("/normal/20","近",0,0);
        var cast=new TrialWord("/cast/20","步行",1,1,"普通读条");
        var interrupt=new TrialWord("/interrupt/20","计步",1e100,2,"？针刺");
        var shield=new TrialWord("/shield/20","计数",1e250,3);
        var scene=new[]{normal,cast,interrupt,shield};
        foreach(var top in new[]{3,2,1,0})
        {
            var machine=Machine();var words=scene.Where(w=>w.Priority<=top).ToArray();
            machine.Tick(View("",words),0);
            Check(machine.Target==words.Single(w=>w.Priority==top).Text,"front distance defeated the highest global tier: "+top);
        }
        foreach(var distance in new[]{double.NaN,double.PositiveInfinity,double.MaxValue})
        {
            var machine=Machine();machine.Tick(View("",[normal,interrupt with {DistanceSquared=distance}]),0);
            Check(machine.Target=="计步","invalid/distant coordinates discarded a higher global tier");
        }
        var tied=Machine();tied.Tick(View("",[interrupt with {DistanceSquared=50},shield with {Priority=2,DistanceSquared=100}]),0);
        Check(tied.Target=="计步","distance did not order words within the same tier");

        // A priority switch may replace a target, but cannot shorten either delay.
        var preempt=Machine();
        Check(preempt.Tick(View("",[normal]),0)=='j',"preemption setup");
        Check(preempt.Tick(View("j",[normal,interrupt]),20)==null && preempt.Tick(View("j",[normal,interrupt]),99)==null,"urgent target bypassed the current key interval");
        Check(preempt.Tick(View("j",[normal,interrupt]),100)=='\b',"higher-tier target did not preempt at the next permitted key");
        Check(preempt.Tick(View("",[normal,interrupt]),150)==null,"clearing bypassed key interval");
        preempt.Tick(View("",[normal,interrupt]),200);
        preempt.Tick(View("",[normal,interrupt]),210); // clear completes; word interval ends at 610
        Check(preempt.Tick(View("",scene),220)==null && preempt.Tick(View("",scene),609)==null,"shield bypassed the between-word delay");
        Check(preempt.Tick(View("",scene),610)=='j' && preempt.Target=="计数","did not choose the globally highest current tier after the interval");
        var withNearShield=scene.Append(normal with {Path="/nearshield/20",Priority=3}).ToArray();
        Check(preempt.Tick(View("j",withNearShield),710)=='i' && preempt.Target=="计数","same-tier distance changes interrupted a locked word");
        Check(preempt.CompletedCount==0,"cleared partial inputs counted as sent");

        foreach(var priority in new[]{1,2,3})
        {
            var machine=Machine(1600);
            machine.Tick(View("",[normal]),0);
            machine.Tick(View("j",[normal],new(){{1,"近"}}),100);
            Check(machine.Tick(View("近",[normal]),300)=='\n',"cooldown setup did not send");
            var urgent=interrupt with {Priority=priority};var words=new[]{normal,urgent};
            Check(machine.Tick(View("",words),350)==null,"urgent word bypassed submit feedback interval");
            machine.Tick(View("",words),550); // word delay begins; no action before 2150
            Check(machine.Tick(View("",words),600)==null && machine.Tick(View("",words),2149)==null,"priority tier bypassed configured word interval: "+priority);
            Check(machine.Tick(View("",words),2150)=='j' && machine.Target=="计步","distant high-priority word not selected once delay elapsed: "+priority);
        }
        var reset=Machine();reset.Tick(View("",[normal]),0);reset.PauseForScene("失焦",20);
        Check(reset.Tick(View("",[interrupt]),50)==null,"scene reset erased the pending key interval");
        Check(reset.Tick(View("",[interrupt]),100)=='j',"did not resume at the permitted key time");
        var unreadable=Machine();unreadable.Tick(View("",[normal]),0);
        Check(unreadable.Tick(View("j",[normal,new("/unknown/20","龘",0,3)]),100)=='i',"unreadable urgent word caused repeated clearing");
        Console.WriteLine("PASS: global shield/cast tiers beat any front distance; same-tier distance only; every key, clear, switch and between-word delay retained; no urgent timing bypass.");
    }
}
