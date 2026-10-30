using System.Text.Json;
using MakoMaster;

internal static class RuntimeLearningTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static DictionaryEntry Entry(string word, params string[] syllables)
        => new(word, syllables, string.Concat(syllables), string.Join("'", syllables));
    private static void Write(string folder, params DictionaryEntry[] words)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"learned-dictionary.json"), JsonSerializer.Serialize(words));
    }
    private static int Count(string folder) => JsonSerializer.Deserialize<DictionaryEntry[]>(
        File.ReadAllText(Path.Combine(folder,"learned-dictionary.json")))!.Length;

    internal static void Run()
    {
        var root = Path.Combine(AppContext.BaseDirectory,"test-data","always-learn-"+Guid.NewGuid().ToString("N"));
        var first = Path.Combine(root,"versions","1.0.0.0");
        var second = Path.Combine(root,"versions","2.0.0.0");
        var third = Path.Combine(root,"versions","3.0.0.0");
        var config = Path.Combine(root,"stable-config");
        var store = new VocabularyStore(first,config);
        Check(!store.PersistenceEnabled && !Directory.Exists(config),"empty learning startup created files");
        store.RecordFallback("计步","jibu","test");
        store.LearnComposition(Entry("计步","ji","bu"));
        Check(Count(config)==1 && Count(first)==1,"learning was not saved immediately with logs disabled");
        Check(!File.Exists(Path.Combine(config,"fallback-words.json")),"diagnostics saved with logs disabled");
        store.PersistenceEnabled=true;store.Flush();
        Check(File.Exists(Path.Combine(config,"fallback-words.json")),"enabled diagnostic saving failed");
        var fallbackBefore=File.ReadAllText(Path.Combine(config,"fallback-words.json"));
        store.PersistenceEnabled=false;
        store.RecordFallback("近","jin","off");
        store.LearnComposition(Entry("近","jin"));
        Check(Count(config)==2 && Count(first)==2,"disabling logs disabled learned-word saving");
        Check(File.ReadAllText(Path.Combine(config,"fallback-words.json"))==fallbackBefore,"disabled diagnostics changed");
        store.LearnComposition(Entry("计，步！","ji","bu"));
        Check(Count(config)==2,"punctuated duplicate increased vocabulary count");

        var legacy=Path.Combine(root,"versions","0.9.0.0");
        Write(legacy,Entry("计数","ji","shu"));
        Write(second,Entry("步行","bu","xing"));
        var updated = new VocabularyStore(second,config,[legacy,first]);
        Check(updated.LearnedCount==4 && updated.EffectiveDictionary.Count==4,"version update lost local/shipped/legacy learning");
        Check(Count(second)==4 && Count(config)==4,"merged cumulative copy was not synchronized");
        Write(third); // Simulate a new package with an empty learned file.
        var later = new VocabularyStore(third,config);
        Check(later.LearnedCount==4 && Count(third)==4,"empty/new package reset cumulative learning");
        var reloaded = new VocabularyStore(third,config,[first,second,legacy]);
        Check(reloaded.LearnedCount==4,"repeated migration duplicated entries");
        var shared = JsonDocument.Parse(File.ReadAllText(Path.Combine(third,"learned-dictionary.json")));
        Check(shared.RootElement.EnumerateArray().All(e=>e.EnumerateObject().Select(p=>p.Name)
            .Order().SequenceEqual(new[]{"Pinyin","Segmented","Syllables","Word"})),"shareable dictionary contains non-vocabulary data");

        var observedFolder=Path.Combine(root,"observed");
        var observed=new VocabularyStore(observedFolder);
        var at=DateTimeOffset.UtcNow;
        var visible=new[]{new TrialWord("/word","计步")};
        observed.Observe(new(true,"ji'bu",visible,new Dictionary<int,string>()),at);
        observed.Observe(new(true,"计步",visible,new Dictionary<int,string>()),at.AddMilliseconds(100));
        observed.Observe(new(true,"",[new("/other","近")],new Dictionary<int,string>()),at.AddMilliseconds(200));
        Check(Count(observedFolder)==1,"observed confirmed learning did not save immediately");

        var settingsFile=Path.Combine(root,"prefs","auto-entry.json");
        Check(AutoEntrySettings.Load(settingsFile).Enabled,"auto-entry not enabled by default");
        new AutoEntrySettings(false).Save(settingsFile);
        Check(!AutoEntrySettings.Load(settingsFile).Enabled,"unchecked auto-entry preference did not persist");

        var gate=new AutoEntrySession();
        Check(gate.Tick(true,false,false,false)==AutoEntryAction.None,"started outside minigame");
        Check(gate.Tick(true,true,false,true)==AutoEntryAction.None,"automatic entry took over a manual run or chest/travel operation");
        Check(gate.Tick(true,true,false,false)==AutoEntryAction.Start && gate.OwnsRun,"game entry did not start");
        Check(gate.Tick(true,false,false,true)==AutoEntryAction.None && gate.OwnsRun,"wave/loading gap ended automatic game");
        Check(gate.Tick(true,true,false,true)==AutoEntryAction.None,"duplicate start in same game");
        Check(gate.Tick(true,false,true,true)==AutoEntryAction.Stop && !gate.OwnsRun,"results did not stop automatic game");
        Check(gate.Tick(true,false,true,false)==AutoEntryAction.None,"results screen restarted run");
        Check(gate.Tick(true,true,false,false)==AutoEntryAction.None,"stale game addons after results restarted typing");
        gate.Tick(true,false,true,false,canRearm:true); // Confirmed lobby or departure from the game territory.
        Check(gate.Tick(true,true,false,false)==AutoEntryAction.Start,"next game did not rearm");
        gate.SuppressCurrentGame();
        Check(gate.Tick(true,true,false,false)==AutoEntryAction.None,"manual stop restarted during same game");
        gate.Tick(true,false,false,false);
        Check(gate.Tick(true,true,false,false)==AutoEntryAction.None,"wave gap reset manual stop suppression");
        gate.Tick(true,false,true,false,canRearm:true);
        Check(gate.Tick(false,true,false,false)==AutoEntryAction.None,"unchecked setting auto-started");
        Check(gate.Tick(true,true,false,false)==AutoEntryAction.Start,"re-enabled entry did not start");
        Check(gate.Tick(false,true,false,true)==AutoEntryAction.Stop,"unchecking did not stop owned automatic typing");
        Check(gate.Tick(true,true,false,false)==AutoEntryAction.None,"toggle restarted the same stopped game");
        var manual=new AutoEntrySession();
        Check(manual.Tick(true,true,false,true)==AutoEntryAction.None && !manual.OwnsRun,"manual loop was taken over");
        Check(manual.Tick(true,false,true,true)==AutoEntryAction.None,"manual loop stopped at results");
        Console.WriteLine("PASS: immediate learning with logs off, stable config and DLL-side cumulative JSON; migration and dedup across updates; diagnostic opt-in; vocabulary-only sharing; auto-entry default/persistence, single-game results, manual stop, wave gaps and manual-loop ownership.");
    }
}
