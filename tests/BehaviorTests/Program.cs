using MakoMaster;
using System.Text.Json;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var dictionary = new Dictionary<string,DictionaryEntry>
{
    ["计步"] = new("计步", ["ji","bu"], "jibu", "ji'bu"),
    ["计数"] = new("计数", ["ji","shu"], "jishu", "ji'shu"),
    ["步行"] = new("步行", ["bu","xing"], "buxing", "bu'xing"),
    ["近"] = new("近", ["jin"], "jin", "jin"),
};
var timing = new TimingSettings(100,100,300,300);
Check(WordMatching.Same("计，步！", "计步"), "punctuation equivalence");

// Exact whole-word candidate appears after only j; selected before ibu.
var early = new TrialMachine(dictionary,0,new Random(1),true,timing);
var earlyWords = new[] {new TrialWord("/far/20", "计，步！",100,1,"自爆"),new TrialWord("/near/20","近",1)};
var buffer=""; var keys="";
for(long now=0;now<3000 && early.CompletedCount<1;now+=10)
{
    var candidates = buffer=="j" ? new Dictionary<int,string>{{3,"计步"}} : new();
    var key=early.Tick(new(true,buffer,now>100?[]:earlyWords,candidates),now);
    if(key==null)continue;
    keys+=key;
    if(key is >= 'a' and <= 'z')buffer+=key;
    else if(key=='3')buffer="计步";
    else if(key=='\n')buffer="";
}
Check(keys=="j3\n", "skill priority, early candidate, punctuation and disappeared enemy: "+keys);
Check(!early.Finished && early.CompletedCount==1,"continuous mode must remain armed");

// Whole word: initial page + one PageDown, clear, then character search.
var fallback=new TrialMachine(dictionary,0,new Random(2),true,timing);
buffer="";keys="";var page=1;var charMode=false;var fullTurns=0;var charTurns=0;var backspaces=0;
for(long now=0;now<30000 && fallback.CompletedCount<1;now+=10)
{
    charMode |= fallback.Result.Contains("逐字");
    var candidates=new Dictionary<int,string>();
    if(!charMode)candidates[1]="记步";
    else if(buffer.StartsWith("计"))candidates[2]="步";
    else if(page>=2)candidates[4]="计";
    else candidates[1]="记";
    var key=fallback.Tick(new(true,buffer,[new("/x/20","计步",0)],candidates,$"page{page}",4),now);
    if(key==null)continue;
    keys+=key;
    if(key is >= 'a' and <= 'z')buffer+=key;
    else if(key=='\f'){page++;if(charMode)charTurns++;else fullTurns++;}
    else if(key=='\b'){buffer=buffer[..^1];backspaces++;page=1;}
    else if(key=='4'){buffer="计";page=1;}
    else if(key=='2'){buffer="计步";page=1;}
    else if(key=='\n')buffer="";
}
Check(fallback.CompletedCount==1 && fullTurns==1 && charTurns>=1 && backspaces==4,
    $"paging/clear/character fallback failed: submitted={fallback.CompletedCount}, full={fullTurns}, chars={charTurns}, back={backspaces}, state={fallback.Result}");

// Unknown whole word: source character readings from other dictionary entries.
var noWhole=dictionary.Where(p=>p.Key!="计步").ToDictionary();
var perChar=new TrialMachine(noWhole,0,new Random(3),true,timing);
buffer=""; keys="";
for(long now=0;now<5000 && perChar.CompletedCount<1;now+=10)
{
    var candidates=new Dictionary<int,string>();
    if(buffer=="j")candidates[1]="计";
    if(buffer=="计b")candidates[2]="步";
    var key=perChar.Tick(new(true,buffer,[new("/x/20","计步")],candidates),now);
    if(key==null)continue;keys+=key;
    if(key is >= 'a' and <= 'z')buffer+=key;
    else if(key=='1')buffer="计";else if(key=='2')buffer="计步";else if(key=='\n')buffer="";
}
Check(keys=="j1b2\n", "unknown whole word did not use early character selection: "+keys);

var resolver=new WordResolver();
var skills=new Dictionary<string,string>{{"/x","臭气"}};
var first=resolver.Resolve([new("/x/20","计步",true,true,10)],dictionary,skills);
var hidden=resolver.Resolve([new("/x/20","",false,true,10)],dictionary,skills);
Check(first[0].Priority==1 && hidden[0].Text=="计步" && hidden[0].Obscured,"skill-slot matching and obscured-word memory");
var punct=resolver.Resolve([new("/q/20","计步？",true,true,10)],dictionary,new Dictionary<string,string>());
Check(punct.Count==1 && WordMatching.Same(punct[0].Text,"计步"),"question punctuation mistaken for a mask");
Check(resolver.Resolve([new("/x/20","计步",false,false,10)],dictionary,new Dictionary<string,string>()).Count==0,"dead slot retained as live target");
Check(resolver.Resolve([new("/x/20","计步",false,false,10)],dictionary,skills).Single().Priority==1,"active cast did not identify an obscured enemy slot");

// Long scene changes never finish continuous mode.
var waiting=new TrialMachine(dictionary,0,new Random(4),true,timing);
waiting.Tick(new(false,"",[],new Dictionary<int,string>()),3_600_000);
Check(!waiting.Finished,"idle timeout disarmed automation");
var firstKey=waiting.Tick(new(true,"",[new("/x/20","计步")],new Dictionary<int,string>()),3_600_010);
Check(firstKey=='j',"did not resume after a long stage transition");
waiting.Stop("UI");
Check(waiting.Tick(new(true,"",earlyWords,new Dictionary<int,string>()),3_600_020)==null,"manual UI stop emitted a key");

// Persist every queued record across rotated segments.
var folder=Path.Combine(AppContext.BaseDirectory,"test-data","recording-"+Guid.NewGuid().ToString("N"));
var session=new CaptureSession(Path.Combine(folder,"session.jsonl"),512);
for(var i=0;i<100;i++)Check(session.TryWrite(new {Kind="Sample",Index=i,Text="开发数据"}),"record enqueue failed");
session.Complete();await session.Completion.WaitAsync(TimeSpan.FromSeconds(10));
if(session.Error!=null)throw session.Error;
var parts=Directory.GetFiles(folder,"*.jsonl");
var indices=parts.SelectMany(File.ReadAllLines).Select(line=>JsonDocument.Parse(line).RootElement.GetProperty("Index").GetInt32()).Order().ToArray();
Check(parts.Length>1 && indices.SequenceEqual(Enumerable.Range(0,100)),"rotation lost/overwrote development data");

Console.WriteLine("PASS: skill priority; punctuation; early whole/character candidates; next-page fallback; clearing; teammate disappearance; obscured memory; missing-whole-word spelling; long cross-stage wait; UI stop; lossless log rotation.");

// All target tiers use the same final distance tie-breaker.
var tiers = new WordResolver().Resolve([
    new("/barrier/20","计步",true,true,9999,0xFF74F8A0),
    new("/interrupt/20","计数",true,true,500),
    new("/cast/20","步行",true,true,2),
    new("/normal/20","近",true,true,1)
], dictionary, new Dictionary<string,string>{{"/interrupt","？针刺"},{"/cast","臭气"}}, new HashSet<string>{"？针刺"});
Check(tiers.OrderByDescending(w=>w.Priority).ThenBy(w=>w.DistanceSquared).Select(w=>w.Priority).SequenceEqual(new[]{3,2,1,0}),"barrier/interruptible/other/normal tiers");
var tierMachine = new TrialMachine(dictionary,0,new Random(1),true,timing);
tierMachine.Tick(new(true,"",tiers,new Dictionary<int,string>()),0);
Check(tierMachine.Target=="计步","green barrier did not outrank all casts");
Check(new TimingSettings() == new TimingSettings(40,100,800,1600),"new default timing ranges");
Check(new TimingSettings(10,30,100,200).ValidationError==null,"valid short key timing rejected");

// Count a whole result once, never count individual waves or loading, exit the last result before stopping.
var loop=new RoundLoop(2);
var home=new RoundView(true,false,false,false,false,false);
RoundAction Action(RoundView v,long t) { var action=loop.Tick(v,t); if(action!=RoundAction.None && action!=RoundAction.Finish)loop.ActionSent(action,t); return action; }
Check(Action(home,0)==RoundAction.Interact,"loop failed to interact with selected NPC");
Check(Action(home with {Entry=true},2500)==RoundAction.Apply,"loop did not apply");
Check(Action(home,5000)==RoundAction.None,"queued registration repeated NPC interaction");
Check(Action(home with {Confirm=true},7500)==RoundAction.Accept,"loop did not accept pop");
Check(Action(home with {Confirm=true},10000)==RoundAction.None,"still-visible pop was accepted twice");
var game=new RoundView(false,false,true,false,false,false);
Check(Action(game,11000)==RoundAction.None && loop.Completed==0,"game should type without counting a round");
Check(Action(new(false,true,false,false,false,false),12000)==RoundAction.None && loop.Completed==0,"loading counted as round completion");
Action(game,13000);
var result=new RoundView(false,false,false,false,false,true);
Check(Action(result,15000)==RoundAction.Exit && loop.Completed==1,"first result not exited or counted");
Action(result,17500);
Check(loop.Completed==1,"same result counted twice");
Check(Action(home,18000)==RoundAction.None,"return home requires stabilization");
Check(Action(home,20000)==RoundAction.Interact,"next round did not begin");
Action(home with {Entry=true},22500); Action(game,25000);
Check(Action(result,30000)==RoundAction.Exit && loop.Completed==2,"last result was not exited");
Check(Action(home,32500)==RoundAction.None,"stopped before leaving final result");
Check(Action(home,34500)==RoundAction.Finish,"finite loop did not stop at requested count");
var stale=new RoundLoop(1);
Check(stale.Tick(result,0)==RoundAction.None && stale.Completed==0,"pre-existing result counted as a new game");

// Both dangerous skills gate every key; overlapping casts extend the pause.
var gate=new SkillPauseGate();
Check(!gate.Tick([new("/a","运动体探知干扰器",5)],0),"paused more than half a second early with precise data");
Check(!gate.Tick([new("/a","运动体探知干扰器",0.501)],4499),"precise cast boundary too early");
Check(gate.Tick([new("/a","运动体探知干扰器",0.5)],4500),"missing pre-cast half-second pause");
Check(gate.Tick([],5000),"missing post-cast pause");
Check(gate.Tick([],6500) && !gate.Tick([],6501),"post-cast pause did not expire at 1.5 seconds");
Check(!gate.Tick([new("/b","诅咒视线",null)],7000) && gate.AwaitingProgress,"missing timer froze entire cast");
Check(!gate.Tick([new("/b","诅咒视线",null)],9000),"missing timer paused before cast end");
Check(gate.Tick([],10000) && !gate.Tick([],11501),"curse did not resume after disappearance guard");
gate.Reset();
Check(gate.Tick([new("/a","运动体探知干扰器",0.2),new("/b","诅咒视线",0.8)],0),"overlap initial pause");
Check(gate.Tick([new("/b","诅咒视线",0.2)],600),"overlap gap incorrectly resumed typing");
Check(gate.Tick([],800) && !gate.Tick([],2301),"overlapping guards failed to expire");

// Pause/resume retains the locked composition and does not trigger an input timeout.
var frozen=new TrialMachine(dictionary,0,new Random(1),true,timing);
Check(frozen.Tick(new(true,"",[new("/x/20","计步")],new Dictionary<int,string>()),0)=='j',"freeze test setup");
frozen.ExtendWait(10000);
Check(frozen.Tick(new(true,"j",[new("/x/20","计步")],new Dictionary<int,string>()),10100)=='i',"paused word restarted or timed out");

// Missing whole words and successful per-character compositions are durable across reloads.
var learningFolder=Path.Combine(folder,"learning"); Directory.CreateDirectory(learningFolder);
File.WriteAllText(Path.Combine(learningFolder,"dictionary.json"),JsonSerializer.Serialize(noWhole.Values));
var store=new VocabularyStore(learningFolder) { PersistenceEnabled = true };
store.RecordFallback("计，步！","jibu","候选缺失");
Check(perChar.ComposedEntries.Count==1,"verified per-character composition was not reported");
store.LearnComposition(perChar.ComposedEntries.Dequeue());
var reopened=new VocabularyStore(learningFolder);
Check(reopened.EffectiveDictionary["计步"].Pinyin=="jibu","character composition not learned across reload");
Check(File.ReadAllText(Path.Combine(learningFolder,"fallback-words.json")).Contains("jibu"),"fallback record was not persisted");
Console.WriteLine("PASS: four target tiers; new timing defaults; finite NPC/registration/pop/result loop; duplicate-result guard; both dangerous skills; overlapping pauses; locked-word resume; durable fallback and composed-word learning.");

// Real IME regression: strategic automaton stalled at zhan'lü / 战lü because v != ü.
Check(WordMatching.InputBuffer("zhan'lüe") == "zhanlve", "IME ü feedback does not match typed v");
Check(WordMatching.InputBuffer("战lu\u0308e") == "战lve", "decomposed IME umlaut not normalized");
var strategicEntry = new DictionaryEntry("战略机兵", ["zhan","lve","ji","bing"], "zhanlvejibing", "zhan'lve'ji'bing");
var strategicBank = new Dictionary<string,DictionaryEntry> { [strategicEntry.Word] = strategicEntry };
foreach (var useFallback in new[]{false,true})
{
    var machine = new TrialMachine(strategicBank,0,new Random(1),true,timing);
    var typed = ""; var emitted = ""; var fallbackActive = false;
    for (long t=0;t<40000 && machine.CompletedCount==0;t+=10)
    {
        fallbackActive |= machine.Result.Contains("逐字");
        var display = typed.Replace('v','ü');
        if (display.StartsWith("zhanl",StringComparison.Ordinal)) display=display.Insert(4,"'");
        var choices = new Dictionary<int,string>();
        if (!useFallback && typed == "zhanlvejibing") choices[1]="战略机兵";
        if (fallbackActive)
        {
            if (typed=="zhan")choices[1]="战";
            if (typed=="战lve")choices[2]="略";
            if (typed=="战略ji")choices[3]="机";
            if (typed=="战略机bing")choices[4]="兵";
        }
        var k=machine.Tick(new(true,display,[new("/x/20","战略机兵")],choices,"page1",1),t);
        if(k==null)continue;
        emitted+=k;
        if(k is >= 'a' and <= 'z') typed+=k;
        else if(k=='\b') typed=typed[..^1];
        else if(k=='1') typed=fallbackActive?"战":"战略机兵";
        else if(k=='2') typed="战略";
        else if(k=='3') typed="战略机";
        else if(k=='4') typed="战略机兵";
        else if(k=='\n') typed="";
    }
    Check(machine.CompletedCount==1, $"strategic word failed with IME umlaut; fallback={useFallback}, state={machine.Result}");
    if(!useFallback)Check(emitted=="zhanlvejibing1\n", "strategic whole word unexpectedly cleared/restarted: "+emitted);
    else Check(emitted.Count(c=>c=='\f')==1 && machine.ComposedEntries.Count==1,"strategic character fallback failed");
}
var barrierResolver = new WordResolver();
var barrierFrames=barrierResolver.Resolve([new("/shield/20","计步",true,true,9999,0x8074F8A0)],dictionary,new Dictionary<string,string>());
Check(barrierFrames.Single().Priority==3,"transparent green shield lost highest priority");
barrierFrames=barrierResolver.Resolve([new("/shield/20","计步",true,true,9999,0xFFFFFFFF)],dictionary,new Dictionary<string,string>{{"/shield","自爆"}},new HashSet<string>{"自爆"});
Check(barrierFrames.Single().Priority==3,"green shield color flash demoted below interruptible skill");
Check(barrierResolver.Resolve([new("/shield/20","近",true,true,1)],dictionary,new Dictionary<string,string>()).Single().Priority==0,"shield priority leaked to a replacement word");
barrierResolver.Reset();
Check(barrierResolver.Resolve([new("/shield/20","计步",true,true,1)],dictionary,new Dictionary<string,string>()).Single().Priority==0,"shield priority survived scene reset");
Console.WriteLine("PASS: strategic word whole/character paths with real IME ü formatting; transparent/flash shield remains highest; shield state clears on replacement and scene change.");

// Once Enter is emitted, the same live target must never be selected again, even after failure.
var once = new TrialMachine(dictionary,0,new Random(1),true,timing);
var activeWords=new[]{new TrialWord("/first/20","计步",1,3),new TrialWord("/second/20","近",2)};
var onceBuffer="";var onceKeys="";var firstSubmissions=0;
for(long t=0;t<15000;t+=10)
{
    var candidates=new Dictionary<int,string>();
    if(onceBuffer=="j")candidates[1]="计步";
    if(onceBuffer=="j" && once.Target=="近")candidates[2]="近";
    var k=once.Tick(new(true,onceBuffer,activeWords,candidates),t);
    if(k==null)continue;onceKeys+=k;
    if(k is >= 'a' and <= 'z')onceBuffer+=k;
    else if(k=='1')onceBuffer="计步";
    else if(k=='2')onceBuffer="近";
    else if(k=='\n'){if(once.Target=="计步")firstSubmissions++; /* failed send leaves Chinese in the input */}
    else if(k=='\b')onceBuffer=onceBuffer[..^1];
}
Check(firstSubmissions==1 && once.CompletedCount==2,"failed submitted target was retried or next word was not selected: "+onceKeys);
once.Tick(new(true,"",[],new Dictionary<int,string>()),16000);
Check(once.Tick(new(true,"",activeWords,new Dictionary<int,string>()),17000)=='j',"same text in a new appearance should be eligible");

// Live cast style overrides historical knowledge, in either direction.
var liveCast=new WordResolver().Resolve([
    new("/non/20","计步",true,true,1),new("/interrupt/20","计数",true,true,9999),new("/normal/20","近",true,true,0)
],dictionary,new Dictionary<string,string>{{"/non","旧技能"},{"/interrupt","新技能"}},new HashSet<string>{"旧技能"},new Dictionary<string,bool>{{"/non",false},{"/interrupt",true}});
Check(liveCast.Single(w=>w.Text=="计步").Priority==1 && liveCast.Single(w=>w.Text=="计数").Priority==2,"current cast style did not override skill history");
Check(liveCast.OrderByDescending(w=>w.Priority).First().Text=="计数","interruptible cast failed to outrank closer uninterruptible cast");

// Infer timing from a bar filling over five seconds; do not freeze its first 4.5 seconds.
var progressGate=new SkillPauseGate();
for(long t=0;t<4500;t+=50)
    Check(!progressGate.Tick([new("/progress","运动体探知干扰器",null,t/5000.0)],t),"progress-derived pause began before final half-second");
Check(progressGate.Tick([new("/progress","运动体探知干扰器",null,0.9)],4500),"progress-derived pause did not begin in final half-second");
Check(progressGate.Tick([],5000) && progressGate.Tick([],6500) && !progressGate.Tick([],6501),"progress-derived post-cast guard wrong");
Console.WriteLine("PASS: failed submissions are never retried while target remains; replacement targets rearm; live cast styles outrank history; fill-progress inference pauses only final/post half-second.");

// Replay only anonymized timing/progress samples from the observed native fill node.
var capturedSamples=JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"cast-progress.json")))!;
var capturedGate=new SkillPauseGate(); long? firstPausedAt=null; var lastCapturedMs=0L;
foreach(var sample in capturedSamples)
{
    var ms=sample.GetProperty("Ms").GetInt64(); var fraction=sample.GetProperty("Progress").GetDouble();
    var paused=capturedGate.Tick([new("/recorded","运动体探知干扰器",null,fraction)],ms);
    if(paused && firstPausedAt==null)firstPausedAt=ms;
    if(ms<3500)Check(!paused,"recorded progress paused during the early cast");
    lastCapturedMs=ms;
}
Check(firstPausedAt is >=3500 and <=4300,"recorded progress did not gate near the end: "+firstPausedAt);
Check(capturedGate.Tick([],lastCapturedMs+100),"recorded cast end missing guard");
Check(!capturedGate.Tick([],lastCapturedMs+1601),"recorded cast guard did not expire");
Console.WriteLine("PASS: anonymized actual native cast-fill samples pause near cast completion, not its beginning.");

// Default startup and in-memory learning must not create any directories or files.
var disabledFolder=Path.Combine(AppContext.BaseDirectory,"test-data","no-save-"+Guid.NewGuid().ToString("N"));
var memoryOnly=new VocabularyStore(disabledFolder);
Check(!memoryOnly.PersistenceEnabled && !Directory.Exists(disabledFolder),"default store created a runtime directory");
memoryOnly.RecordFallback("计步","jibu","内存记录");
memoryOnly.LearnComposition(new("计步",["ji","bu"],"jibu","ji'bu"));
memoryOnly.Observe(new(true,"",[new("/memory/20","近")],new Dictionary<int,string>()),DateTimeOffset.UtcNow);
memoryOnly.Flush();
Check(memoryOnly.EffectiveDictionary.ContainsKey("计步") && !Directory.Exists(disabledFolder),"disabled saving broke memory learning or wrote files");
memoryOnly.PersistenceEnabled=true;memoryOnly.Flush();
Check(File.Exists(Path.Combine(disabledFolder,"learned-dictionary.json")) && File.Exists(Path.Combine(disabledFolder,"fallback-words.json")),"explicit enabling did not persist pending records");
var savedContents=File.ReadAllText(Path.Combine(disabledFolder,"fallback-words.json"));
memoryOnly.PersistenceEnabled=false;memoryOnly.RecordFallback("近","jin","关闭后内存记录");memoryOnly.Flush();
Check(File.ReadAllText(Path.Combine(disabledFolder,"fallback-words.json"))==savedContents,"turning saving off still changed a file");
Console.WriteLine("PASS: files/directories are absent by default; learning stays in memory; opt-in persists; opt-out stops writes.");

// Higher tiers appearing mid-word preempt immediately, even during a key delay.
var preempt=new TrialMachine(dictionary,0,new Random(1),true,timing);
var normalWord=new TrialWord("/ordinary/20","近",1,0);
var interruptWord=new TrialWord("/interrupt/20","计步",9999,2,"自爆");
var shieldWord=new TrialWord("/shield/20","计数",99999,3);
Check(preempt.Tick(new(true,"",[normalWord],new Dictionary<int,string>()),0)=='j',"preemption setup did not start normal word");
Check(preempt.Tick(new(true,"j",[normalWord,interruptWord],new Dictionary<int,string>()),20)=='\b',"new interruptible cast waited for the normal word or its timer");
preempt.Tick(new(true,"",[normalWord,interruptWord],new Dictionary<int,string>()),120);
preempt.Tick(new(true,"",[normalWord,interruptWord],new Dictionary<int,string>()),130);
Check(preempt.Tick(new(true,"",[normalWord,interruptWord],new Dictionary<int,string>()),140)=='j' && preempt.Target=="计步","interruptible cast was not selected immediately after clearing");
Check(preempt.Tick(new(true,"j",[normalWord,interruptWord,shieldWord],new Dictionary<int,string>()),150)=='\b',"new shield did not preempt an interruptible cast");
preempt.Tick(new(true,"",[normalWord,interruptWord,shieldWord],new Dictionary<int,string>()),250);
preempt.Tick(new(true,"",[normalWord,interruptWord,shieldWord],new Dictionary<int,string>()),260);
Check(preempt.Tick(new(true,"",[normalWord,interruptWord,shieldWord],new Dictionary<int,string>()),270)=='j' && preempt.Target=="计数","shield did not become the selected target");
Check(preempt.CompletedCount==0,"abandoned partial inputs were counted as sent");
Check(preempt.Tick(new(true,"j",[normalWord,interruptWord,shieldWord],new Dictionary<int,string>()),370)=='i',"lower-tier cast preempted an active shield");
var equalTier=new TrialMachine(dictionary,0,new Random(1),true,timing);
Check(equalTier.Tick(new(true,"",[new("/far/20","计步",9999,1)],new Dictionary<int,string>()),0)=='j',"equal-tier setup");
Check(equalTier.Tick(new(true,"j",[new("/far/20","计步",9999,1),new("/near/20","近",1,1)],new Dictionary<int,string>()),100)=='i' && equalTier.Target=="计步","same-tier distance change caused a mid-word switch");
var untypable=new TrialMachine(dictionary,0,new Random(1),true,timing);
untypable.Tick(new(true,"",[normalWord],new Dictionary<int,string>()),0);
Check(untypable.Tick(new(true,"j",[normalWord,new("/missing/20","龘",0,3)],new Dictionary<int,string>()),100)=='i',"unreadable urgent word caused an endless clear/restart");
Check(SkillPauseGate.BeforeMs==500 && SkillPauseGate.AfterMs==1500,"dangerous-skill pause interval constants wrong");
Console.WriteLine("PASS: normal-to-cast and cast-to-shield preemption during pending delays; no same-tier thrashing; unreadable-target guard; pause ends 1.5 seconds after cast.");

// Confirmed skill classification survives transient hidden interrupt markers.
var needleWords=new WordResolver().Resolve([
    new("/needle/20","计步",true,true,9999),new("/cast/20","计数",true,true,1),new("/shield/20","近",true,true,99999,0xFF74F8A0)
],dictionary,new Dictionary<string,string>{{"/needle","？针刺"},{"/cast","其他技能"}},new HashSet<string>(),new Dictionary<string,bool>{{"/needle",false},{"/cast",false}});
Check(needleWords.Single(w=>w.Text=="计步").Priority==2,"confirmed needle skill was demoted by a hidden marker");
Check(needleWords.OrderByDescending(w=>w.Priority).Select(w=>w.Text).SequenceEqual(new[]{"近","计步","计数"}),"shield/needle/uninterruptible order wrong");
Console.WriteLine("PASS: confirmed needle interruptibility survives hidden markers and stays below shields.");

var bombWords=new WordResolver().Resolve([
    new("/bomb/20","计步",true,true,9999),new("/cast/20","计数",true,true,1),new("/shield/20","近",true,true,99999,0xFF74F8A0)
],dictionary,new Dictionary<string,string>{{"/bomb","霹雳炸弹"},{"/cast","其他技能"}},new HashSet<string>(),new Dictionary<string,bool>{{"/bomb",false},{"/cast",false}});
Check(bombWords.Single(w=>w.Text=="计步").Priority==2,"confirmed bomb skill was demoted by a hidden marker");
Check(bombWords.OrderByDescending(w=>w.Priority).Select(w=>w.Text).SequenceEqual(new[]{"近","计步","计数"}),"shield/bomb/uninterruptible order wrong");
Console.WriteLine("PASS: confirmed bomb interruptibility survives hidden markers and stays below shields.");
