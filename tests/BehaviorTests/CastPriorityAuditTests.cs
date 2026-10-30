using System.Text.Json;
using MakoMaster;

internal static class CastPriorityAuditTests
{
    internal static void Run()
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        static EnemyUiAnchor Read(JsonElement x) => new(x.GetProperty("Slot").GetString()!, x.GetProperty("X").GetSingle(),
            x.GetProperty("Y").GetSingle(), x.GetProperty("Width").GetSingle(), x.GetProperty("Height").GetSingle(),
            x.GetProperty("ScaleX").GetSingle(), x.GetProperty("ScaleY").GetSingle());
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"cast-word-layout.json")));
        var count = 0; var changed = 0;
        foreach (var frame in fixture.RootElement.EnumerateArray())
        {
            var words=frame.GetProperty("Words").EnumerateArray().Select(Read).ToArray();
            var casts=frame.GetProperty("Casts").EnumerateArray().Select(Read).ToArray();
            var matched=CastTargetMatcher.Match(words,casts).ToDictionary(l=>l.CastSlot);
            foreach(var expected in frame.GetProperty("Links").EnumerateArray())
            {
                var cast=expected.GetProperty("CastSlot").GetString()!;
                var word=expected.GetProperty("WordSlot").GetString()!;
                Check(matched.TryGetValue(cast,out var actual) && actual.WordSlot==word && actual.Error<.001,"captured cast assigned to wrong word: "+cast+" => "+word);
                count++; if(cast!=word) changed++;
            }
        }
        Check(count==883 && changed==651,"full captured-layout regression coverage changed");
        var wordAnchor=new EnemyUiAnchor("/word",100,100,100,64,1,1);
        var castAnchor=new EnemyUiAnchor("/different-native-slot",70,180,160,26,1,1);
        Check(CastTargetMatcher.Match([wordAnchor],[castAnchor]).Single().WordSlot=="/word","native slot IDs must not determine ownership");
        Check(CastTargetMatcher.Match([wordAnchor,wordAnchor with{Slot="/overlapping"}],[castAnchor]).Count==0,"ambiguous overlapping layout must not be guessed");
        Check(CastTargetMatcher.Match([wordAnchor],[castAnchor with{X=500}]).Count==0,"distant bars must not bind");
        Check(CastTargetMatcher.Match([wordAnchor with{X=float.NaN}],[castAnchor]).Count==0,"invalid coordinates must not bind");
        Check(CastStyleRules.Interruptible(2,false,false)==true,"interruptible style is known before countdown appears");
        Check(CastStyleRules.Interruptible(1,true,true)==false,"ordinary style must not become interruptible due to unrelated markers");
        var dictionary=JsonSerializer.Deserialize<DictionaryEntry[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"dictionary.json")))!.ToDictionary(e=>WordMatching.Normalize(e.Word));
        const string sentence="我高薪聘请你你可别让我失望";
        Check(dictionary[sentence].Syllables.Length==sentence.Length,"shield phrase must contain complete per-character pronunciation");
        var noPhrase=dictionary.Where(p=>p.Key!=sentence).ToDictionary();
        var oldWords=new TrialMachine(noPhrase,0,continuous:true);
        oldWords.Tick(new(true,"",[new("/shield/20","我高薪聘请你，你可别让我失望",1000,WordPriority.Barrier),new("/normal/20","时间",1)],new Dictionary<int,string>()),0);
        Check(oldWords.Fallbacks.Any(f=>f.Reason.Contains("薪")&&f.Reason.Contains("聘")),"unreadable high-priority words must have an explicit diagnostic");
        var newWords=new TrialMachine(dictionary,0,continuous:true);
        newWords.Tick(new(true,"",[new("/shield/20","我高薪聘请你，你可别让我失望",1000,WordPriority.Barrier),new("/normal/20","时间",1)],new Dictionary<int,string>()),0);
        Check(WordMatching.Same(newWords.Target,sentence),"completed shield phrase must outrank a nearby normal word");
        var skills=new WordResolver().Resolve([new("/actual/20","埃力格",true,true,1000),new("/wrong/20","如月",true,true,1)],dictionary,
            new Dictionary<string,string>{{"/actual","？针刺"}},interruptibleSlots:new Dictionary<string,bool>{{"/actual",true}});
        var skillTrial=new TrialMachine(dictionary,0,continuous:true);
        skillTrial.Tick(new(true,"",skills,new Dictionary<int,string>()),0);
        Check(skillTrial.Target=="埃力格","correctly mapped interruptible target must outrank the previously misassigned closer word");
        Console.WriteLine($"PASS: {count} captured cast-word links ({changed} native-slot mismatches), ambiguous-layout guards, cast-start styles and complete shield phrase priority.");
    }
}
