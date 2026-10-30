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

RetainedInputTests.Run();

// A match on the third subsequent page is selected without falling back to characters.
var thirdPage = new TrialMachine(dictionary,0,new Random(2),true,timing);
buffer=""; keys=""; var page=1; var thirdPageTurns=0;
for(long now=0;now<15000 && thirdPage.CompletedCount<1;now+=10)
{
    var candidates=new Dictionary<int,string>{{1,page==4 ? "计步" : "记步"}};
    var key=thirdPage.Tick(new(true,buffer,[new("/x/20","计步")],candidates,$"page{page}",4),now);
    if(key==null)continue;
    keys+=key;
    if(key is >= 'a' and <= 'z')buffer+=key;
    else if(key=='\f'){page++;thirdPageTurns++;}
    else if(key=='1')buffer="计步";
    else if(key=='\n')buffer="";
}
Check(thirdPage.CompletedCount==1 && thirdPageTurns==3 && keys=="jibu\f\f\f1\n",
    "third subsequent page whole-word selection failed: "+keys);
Check(thirdPage.Fallbacks.Count==0,"third-page match incorrectly recorded fallback");

// Unknown whole word: source character readings from other dictionary entries.
var noWhole=dictionary.Where(p=>p.Key!="计步").ToDictionary();
var perChar=new TrialMachine(noWhole,0,new Random(3),true,timing,imeDictionaryInstalled:false);
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
Check(!gate.Tick([new("/a","运动体探知干扰器",1.001)],3999),"precise cast boundary too early");
Check(gate.Tick([new("/a","运动体探知干扰器",1)],4000),"missing pre-cast one-second pause");
Check(gate.Tick([],5000),"missing post-cast pause");
Check(gate.Tick([],7500) && !gate.Tick([],7501),"post-cast pause did not expire at 2.5 seconds");
Check(!gate.Tick([new("/b","诅咒视线",null)],8000) && gate.AwaitingProgress,"missing timer froze entire cast");
Check(!gate.Tick([new("/b","诅咒视线",null)],9000),"missing timer paused before cast end");
Check(gate.Tick([],10000) && !gate.Tick([],12501),"curse did not resume after disappearance guard");
gate.Reset();
Check(gate.Tick([new("/a","运动体探知干扰器",0.2),new("/b","诅咒视线",0.8)],0),"overlap initial pause");
Check(gate.Tick([new("/b","诅咒视线",0.2)],600),"overlap gap incorrectly resumed typing");
Check(gate.Tick([],800) && !gate.Tick([],3301),"overlapping guards failed to expire");

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
    var typed = ""; var emitted = "";
    for (long t=0;t<40000 && machine.CompletedCount==0;t+=10)
    {
        var display = typed.Replace('v','ü');
        if (display.StartsWith("zhanl",StringComparison.Ordinal)) display=display.Insert(4,"'");
        var choices = new Dictionary<int,string>();
        if (!useFallback && typed == "zhanlvejibing") choices[1]="战略机兵";
        if (useFallback)
        {
            if (typed=="zhanlvejibing")choices[1]="战";
            if (typed=="战lvejibing")choices[2]="略";
            if (typed=="战略jibing")choices[3]="机";
            if (typed=="战略机bing")choices[4]="兵";
        }
        var k=machine.Tick(new(true,display,[new("/x/20","战略机兵")],choices,"page1",1),t);
        if(k==null)continue;
        emitted+=k;
        if(k is >= 'a' and <= 'z') typed+=k;
        else if(k=='1') typed=useFallback?"战lvejibing":"战略机兵";
        else if(k=='2') typed="战略jibing";
        else if(k=='3') typed="战略机bing";
        else if(k=='4') typed="战略机兵";
        else if(k=='\n') typed="";
        else if(k=='\b') throw new Exception("strategic retained composition cleared input");
    }
    Check(machine.CompletedCount==1, $"strategic word failed with IME umlaut; fallback={useFallback}, state={machine.Result}");
    Check(emitted==(useFallback?"zhanlvejibing1234\n":"zhanlvejibing1\n"), "strategic retained composition retyped pinyin: "+emitted);
    if(useFallback)Check(machine.ComposedEntries.Count==1,"strategic retained composition was not queued for learning");
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

// Infer timing from a bar filling over five seconds; do not freeze its first four seconds.
var progressGate=new SkillPauseGate();
for(long t=0;t<4000;t+=50)
    Check(!progressGate.Tick([new("/progress","运动体探知干扰器",null,t/5000.0)],t),"progress-derived pause began before final one-second");
Check(progressGate.Tick([new("/progress","运动体探知干扰器",null,0.8)],4000),"progress-derived pause did not begin in final one-second");
Check(progressGate.Tick([],5000) && progressGate.Tick([],7500) && !progressGate.Tick([],7501),"progress-derived post-cast guard wrong");
Console.WriteLine("PASS: failed submissions are never retried while target remains; replacement targets rearm; live cast styles outrank history; fill-progress inference pauses only final/post one-second.");

// Replay only anonymized timing/progress samples from the observed native fill node.
var capturedSamples=JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"cast-progress.json")))!;
var capturedGate=new SkillPauseGate(); long? firstPausedAt=null; var lastCapturedMs=0L;
foreach(var sample in capturedSamples)
{
    var ms=sample.GetProperty("Ms").GetInt64(); var fraction=sample.GetProperty("Progress").GetDouble();
    var paused=capturedGate.Tick([new("/recorded","运动体探知干扰器",null,fraction)],ms);
    if(paused && firstPausedAt==null)firstPausedAt=ms;
    if(ms<3000)Check(!paused,"recorded progress paused during the early cast");
    lastCapturedMs=ms;
}
Check(firstPausedAt is >=3000 and <=3900,"recorded progress did not gate near the end: "+firstPausedAt);
Check(capturedGate.Tick([],lastCapturedMs+100),"recorded cast end missing guard");
Check(!capturedGate.Tick([],lastCapturedMs+2601),"recorded cast guard did not expire");
Console.WriteLine("PASS: anonymized actual native cast-fill samples pause near cast completion, not its beginning.");

RuntimeLearningTests.Run();

PriorityTimingTests.Run();

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

foreach(var confirmedSkill in new[]{"？针刺","霹雳炸弹","黏菌爆发"})
{
    var resolved=new WordResolver().Resolve([
        new("/skill/20","计步",true,true,9999),new("/other/20","计数",true,true,1),new("/shield/20","近",true,true,99999,0xFF74F8A0)
    ],dictionary,new Dictionary<string,string>{{"/skill",confirmedSkill},{"/other","其他技能"}},new HashSet<string>(),new Dictionary<string,bool>{{"/skill",false},{"/other",false}});
    Check(resolved.Single(w=>w.Text=="计步").Priority==2,"confirmed interruptible skill demoted: "+confirmedSkill);
    Check(resolved.OrderByDescending(w=>w.Priority).Select(w=>w.Text).SequenceEqual(new[]{"近","计步","计数"}),"shield/interruptible/other order wrong: "+confirmedSkill);
}
Console.WriteLine("PASS: needle, thunder bomb and slime burst remain interruptible despite hidden markers.");

// Starting inside a minigame must not register that territory as the NPC's home.
var insideScene=new RoundScene(999,false,true,false,false,false,false);
var insideView=insideScene.ToView(null);
Check(insideView.Game && !insideView.Home,"in-game startup was mistaken for NPC home");
var returningScene=new RoundScene(123,false,true,false,false,false,true);
var returnedView=returningScene.ToView(null);
Check(returnedView.Home && !returnedView.Game,"visible entry NPC did not override stale game UI");
var registrationScene=new RoundScene(123,false,true,true,false,false,false);
Check(registrationScene.ToView(null).Home && !registrationScene.ToView(null).Game,"registration window failed to identify home");
Check(!returningScene.ToView(999).Game && returningScene.ToView(999).Home,"wrong startup territory blocked observed entry NPC");
Check(!(returningScene with {Loading=true}).ToView(123).Home,"loading should never identify home");
Check(!new RoundScene(222,false,false,false,false,false,false).ToView(123).Home,"unrelated territory was accepted as home");
var resumedLoop=new RoundLoop(2);
resumedLoop.Tick(insideView,100);
Check(resumedLoop.Tick(new(false,false,false,false,false,true),1000)==RoundAction.Exit && resumedLoop.Completed==1,"mid-game startup did not recognize its result");
Check(resumedLoop.Tick(returnedView,2000)==RoundAction.None,"return stabilization bypassed");
Check(resumedLoop.Tick(returnedView,3600)==RoundAction.Interact,"stale game UI or wrong original territory stalled the next NPC interaction");
var finalLoop=new RoundLoop(1);
finalLoop.Tick(insideView,100);
finalLoop.Tick(new(false,false,false,false,false,true),1000);
finalLoop.Tick(returnedView,2000);
Check(finalLoop.Tick(returnedView,3600)==RoundAction.Finish,"last round did not stop after returning with stale UI");
Console.WriteLine("PASS: in-game loop startup; NPC/entry evidence overrides stale UI and wrong startup territory; loading/unrelated-zone guards; restart or finish after result.");

InputLanguageTests.Run();

ChestOpeningTests.Run();

Check(TimingSettings.FastPreset==new TimingSettings(20,50,400,800) && TimingSettings.FastPreset.ValidationError==null,"fast preset is not half of defaults");
Check(new TimingSettings()==new TimingSettings(40,100,800,1600),"fast preset changed defaults");
var singleStart=new RoundLoop(1,true);
var priorResult=new RoundView(false,false,false,false,false,true);
Check(singleStart.Tick(priorResult,0)==RoundAction.Exit && singleStart.Completed==0,"old result was counted or not exited before single start");
singleStart.ActionSent(RoundAction.Exit,0);
Check(singleStart.Tick(priorResult,500)==RoundAction.None && singleStart.Completed==0,"old result repeated before action cooldown");
var entryHome=new RoundView(true,false,false,false,false,false);
Check(singleStart.Tick(entryHome,2500)==RoundAction.Interact,"single start did not request NPC interaction");singleStart.ActionSent(RoundAction.Interact,2500);
Check(singleStart.Tick(entryHome with {Entry=true},4500)==RoundAction.Apply,"single start did not request registration");singleStart.ActionSent(RoundAction.Apply,4500);
Check(singleStart.Tick(entryHome with {Confirm=true},6500)==RoundAction.Accept,"single start did not confirm departure");singleStart.ActionSent(RoundAction.Accept,6500);
singleStart.Tick(new(false,false,true,false,false,false),9000);
Check(singleStart.Tick(priorResult,15000)==RoundAction.Exit && singleStart.Completed==1,"single start failed to count its new game");singleStart.ActionSent(RoundAction.Exit,15000);
singleStart.Tick(entryHome,17500);
Check(singleStart.Tick(entryHome,19500)==RoundAction.Finish,"single start began a second game");
Console.WriteLine("PASS: fast preset is half of defaults; non-loop start can clear a prior result, register, finish exactly one game, exit and stop.");

Check(PartyLoopPolicy.Resolve(1,false,1,1)==PartyLoopRole.Disabled,"solo disabled");
Check(PartyLoopPolicy.Resolve(3,false,1,1)==PartyLoopRole.Disabled,"three disabled");
Check(PartyLoopPolicy.Resolve(4,true,1,1)==PartyLoopRole.Disabled,"alliance disabled");
Check(PartyLoopPolicy.Resolve(4,false,1,1)==PartyLoopRole.Leader,"leader");
Check(PartyLoopPolicy.Resolve(4,false,1,2)==PartyLoopRole.Member,"member");
Check(PartyLoopPolicy.Resolve(4,false,null,2)==PartyLoopRole.Waiting,"unknown leader waits");
var follower = new RoundLoop(2);
Check(follower.Tick(home,0,false)==RoundAction.None,"member no interact");
Check(follower.Tick(home with { Entry=true },1,false)==RoundAction.None,"member no apply");
Check(follower.Tick(home with { Confirm=true },2,false)==RoundAction.Accept,"member accepts");
follower.ActionSent(RoundAction.Accept,2);
follower.Tick(game,3000,false);
Check(follower.Tick(result,4000,false)==RoundAction.Exit && follower.Completed==1,"member completes");
follower.ActionSent(RoundAction.Exit,4000);
follower.Tick(home,6000,false);
Check(follower.Tick(home,8000,false)==RoundAction.None,"member waits next");
Check(follower.Tick(home,9000,true)==RoundAction.Interact,"new leader registers");
follower.Tick(game,10000,false);
Check(follower.Tick(result,11000,false)==RoundAction.Exit && follower.Completed==2,"second count");
follower.Tick(home,14000,false);
Check(follower.Tick(home,16000,false)==RoundAction.Finish,"member finishes limit");
Console.WriteLine("PASS: party size, leadership, follower waiting/confirmation/results/limit and leader transfer.");

Check(TimingSettings.DigaPreset == new TimingSettings(10,10,0,0) && TimingSettings.DigaPreset.ValidationError == null,"Diga minimum preset");
Check(new TimingSettings(9,10,0,0).ValidationError != null,"minimum key boundary");
for(var i=0;i<20;i++) Check(TimingSettings.DigaPreset.NextKey(new Random(i))==10 && TimingSettings.DigaPreset.NextWord(new Random(i))==0,"Diga has no random range");
Check(PartyLoopPolicy.ResolveCrossRealm(4,false,true)==PartyLoopRole.Leader,"cross-world leader");
Check(PartyLoopPolicy.ResolveCrossRealm(4,false,false)==PartyLoopRole.Member,"cross-world member without entity IDs");
Check(PartyLoopPolicy.ResolveCrossRealm(4,false,null)==PartyLoopRole.Waiting,"unresolved cross-world identity waits");
Check(PartyLoopPolicy.ResolveCrossRealm(3,false,true)==PartyLoopRole.Disabled,"cross-world three disabled");
Check(PartyLoopPolicy.ResolveCrossRealm(8,false,true)==PartyLoopRole.Disabled,"cross-world eight disabled");
Check(PartyLoopPolicy.ResolveCrossRealm(4,true,true)==PartyLoopRole.Disabled,"cross-world alliance disabled");
Console.WriteLine("PASS: Diga minimum preset and cross-world party role policy.");

// Unlimited runs never finish at a result or after returning home, including followers.
foreach (var initiates in new[] { true, false })
{
    var forever = new RoundLoop(0,true);
    Check(forever.Unlimited,"unlimited run flag");
    Check(forever.Tick(result,0,initiates)==RoundAction.Exit,"unlimited clears old result");
    for(var n=0;n<12;n++)
    {
        long t=10000+n*20000;
        forever.Tick(game,t,initiates);
        Check(forever.Tick(result,t+1000,initiates)==RoundAction.Exit,"unlimited exits result");
        forever.ActionSent(RoundAction.Exit,t+1000);
        forever.Tick(result,t+1100,initiates);
        Check(forever.Completed==n+1,"unlimited counts each result once");
        forever.Tick(home,t+4000,initiates);
        Check(forever.Tick(home,t+6000,initiates)==(initiates?RoundAction.Interact:RoundAction.None),"unlimited restarts or waits for leader");
    }
}
Console.WriteLine("PASS: unlimited solo/leader/follower play continues across results without finishing.");

Check(RunModePolicy.Preset(RunMode.Solo)==new TimingSettings(),"solo uses default preset");
Check(RunModePolicy.Preset(RunMode.Leader)==TimingSettings.FastPreset,"leader uses fast preset");
Check(RunModePolicy.Preset(RunMode.Member)==TimingSettings.FastPreset,"member uses fast preset");
Check(RunModePolicy.Rounds(RunMode.Solo,3,7)==3 && RunModePolicy.Rounds(RunMode.Leader,3,7)==7 && RunModePolicy.Rounds(RunMode.Member,3,7)==0,"mode count routing");
Check(RunModePolicy.Rounds(RunMode.Solo,0,1)==0,"zero keeps unlimited support");
Check(RunModePolicy.CanInitiate(RunMode.Solo) && RunModePolicy.CanInitiate(RunMode.Leader) && !RunModePolicy.CanInitiate(RunMode.Member),"mode NPC and registration permissions");
Check(RunModePolicy.PartyError(RunMode.Solo,PartyLoopRole.Disabled)==null,"solo needs no party");
foreach(var mode in new[]{RunMode.Leader,RunMode.Member})
{
 Check(RunModePolicy.PartyError(mode,PartyLoopRole.Disabled)!=null,"party size rejection");
 Check(RunModePolicy.PartyError(mode,PartyLoopRole.Waiting)!=null,"unknown party cannot register");
}
Check(RunModePolicy.PartyError(RunMode.Leader,PartyLoopRole.Member)!=null,"nonleader cannot use leader mode");
Check(RunModePolicy.PartyError(RunMode.Member,PartyLoopRole.Leader)!=null,"leader cannot use member mode");
Check(RunModePolicy.PartyError(RunMode.Leader,PartyLoopPolicy.ResolveCrossRealm(4,false,true))==null,"cross-world leader mode");
Check(RunModePolicy.PartyError(RunMode.Member,PartyLoopPolicy.ResolveCrossRealm(4,false,false))==null,"cross-world member mode");
foreach(var mode in new[]{RunMode.Solo,RunMode.Leader})
{
 var finiteMode=new RoundLoop(RunModePolicy.Rounds(mode,2,2),true);
 for(int n=0;n<2;n++)
 {
  long t=20000+n*20000;
  finiteMode.Tick(game,t,RunModePolicy.CanInitiate(mode));
  Check(finiteMode.Tick(result,t+1000)==RoundAction.Exit,"mode result exit");
  finiteMode.ActionSent(RoundAction.Exit,t+1000);
  finiteMode.Tick(home,t+4000);
  Check(finiteMode.Tick(home,t+6000)==(n==1?RoundAction.Finish:RoundAction.Interact),"mode finite restart/stop");
 }
}
Console.WriteLine("PASS: four modes, timing presets, independent counts, party role validation and finite round completion.");

Check(!WorldAutomationRules.ShouldRedeem(true,144,10000,9100),"no redemption outside plaza");
Check(!WorldAutomationRules.ShouldRedeem(false,1360,10000,9100),"redemption requires opt-in");
Check(!WorldAutomationRules.ShouldRedeem(true,1360,9100,9100),"redemption threshold is strict greater than");
Check(WorldAutomationRules.ShouldRedeem(true,1360,9101,9100),"redemption above threshold");
Check(!WorldAutomationRules.ShouldRedeem(true,1360,10000,-1),"reject negative threshold");
Check(WorldAutomationRules.PurchaseQuantity(10000,500)==10,"spend all points, not just excess threshold");
Check(WorldAutomationRules.PurchaseQuantity(909,500)==0,"insufficient points");
Check(WorldAutomationRules.PurchaseQuantity(10000,3)==3,"respect inventory capacity");
Check(WorldAutomationRules.PurchaseQuantity(1000000,1000)==99,"bounded purchase batch");
Check(WorldAutomationRules.Receipt(10000,2,900,12,10),"verify exact point and chest receipt");
Check(!WorldAutomationRules.Receipt(10000,2,900,11,10),"partial receipt cannot repeat purchase");
Check(!WorldAutomationRules.Receipt(10000,2,1000,12,10),"wrong currency delta rejected");
Check(WorldAutomationRules.Arrived(new(1,0,1),new(0,0,0)),"NPC interaction distance");
Check(!WorldAutomationRules.Arrived(new(0,10,0),new(0,0,0)),"different floor is not arrival");
var pendingNavigation = new NavigationRequestGate();
pendingNavigation.Begin(); pendingNavigation.RequestStop();
Check(!pendingNavigation.UpdateStop(true,false) && pendingNavigation.Owned,"keep ownership while pending path computes");
Check(pendingNavigation.UpdateStop(false,true),"stop path installed after stop click");
Check(!pendingNavigation.UpdateStop(false,false) && !pendingNavigation.Owned,"release only after path settles");
Check(!pendingNavigation.UpdateStop(false,true),"do not stop unrelated navigation");
Console.WriteLine("PASS: plaza-only strict redemption threshold, spend-all quantities, exact receipts, XYZ arrival and late-path cancellation.");

Check(!WorldAutomationRules.ShouldRedeem(true,1360,900,0),"below-price balance cannot repeatedly trigger redemption");

Check(WorldDialogueRules.MenuChoice(WorldAutomationRules.Guide,"前往键斗广场。"),"guide destination option");
Check(WorldDialogueRules.MenuChoice(WorldAutomationRules.Guide,"进入键斗广场"),"guide entry variant");
Check(!WorldDialogueRules.MenuChoice(WorldAutomationRules.Guide,"不前往键斗广场"),"reject negative guide choice");
Check(!WorldDialogueRules.MenuChoice(WorldAutomationRules.Guide,"键斗广场说明"),"do not select explanation");
Check(WorldDialogueRules.MenuChoice(WorldAutomationRules.Vendor," 其他。 "),"vendor other category punctuation");
Check(WorldDialogueRules.MenuChoice(WorldDialogueRules.RegistrationNpc,"报名参加魔光键影"),"registration menu");
Check(WorldDialogueRules.MenuChoice(WorldDialogueRules.RegistrationNpc,"申请参加"),"registration short option");
Check(!WorldDialogueRules.MenuChoice(WorldDialogueRules.RegistrationNpc,"不参加魔光键影"),"reject negative registration");
Check(WorldDialogueRules.CanHandle(true,WorldAutomationRules.Guide,null),"cleared target does not lose owned dialogue");
Check(!WorldDialogueRules.CanHandle(true,WorldAutomationRules.Guide,WorldAutomationRules.Vendor),"changed target does not advance unrelated dialogue");
Check(!WorldDialogueRules.CanHandle(false,WorldAutomationRules.Guide,null),"do not advance unowned dialogue");
Check(WorldDialogueRules.ClearOnPlazaArrival(WorldAutomationRules.Guide),"guide interaction ends on plaza arrival");
Check(!WorldDialogueRules.ClearOnPlazaArrival(WorldAutomationRules.Vendor),"vendor ownership survives plaza updates");
Check(!WorldDialogueRules.ClearOnPlazaArrival(WorldDialogueRules.RegistrationNpc),"registration ownership survives plaza updates");
Console.WriteLine("PASS: NPC dialogue choices, negative/description exclusions, cleared targets and plaza session ownership.");

Check(WorldAutomationRules.AtStandingPosition(WorldAutomationRules.EntryStandingPosition,WorldAutomationRules.EntryStandingPosition,new(-12.100403f,.33283907f,-31.296265f)),"recorded registration side point allows interaction beyond old 2.8y limit");
Check(WorldAutomationRules.AtStandingPosition(WorldAutomationRules.VendorStandingPosition,WorldAutomationRules.VendorStandingPosition,new(8.194031f,0,-29.160034f)),"recorded exchange side point");
Check(!WorldAutomationRules.AtStandingPosition(new(-9.870851f,.025749326f,-29.448524f),WorldAutomationRules.EntryStandingPosition,new(-12.100403f,.33283907f,-31.296265f)),"old stalled front position is not side-point arrival");
Check(!WorldAutomationRules.ShouldRedeemWhileQueuing(true,false,1360,10000,9100),"idle and manual travel cannot trigger automatic exchange");
Check(WorldAutomationRules.ShouldRedeemWhileQueuing(true,true,1360,10000,9100),"automatic queue permits exchange");
Console.WriteLine("PASS: captured node-less dialogue events, recorded side destinations and queue-only exchange.");

Check(WorldDialogueRules.CanConfirm(true,WorldAutomationRules.Guide,null,"要前往键斗广场吗？",0),"guide yes confirmation belongs to our flow");
Check(!WorldDialogueRules.CanConfirm(false,WorldAutomationRules.Guide,null,"要前往键斗广场吗？",0),"unowned popup must not be confirmed");
Check(!WorldDialogueRules.CanConfirm(true,WorldAutomationRules.Guide,null,"要放弃任务吗？",0),"unrelated popup must not be confirmed");
Check(!WorldDialogueRules.CanConfirm(true,WorldAutomationRules.Vendor,null,"兑换魔光键影宝箱？",0),"no purchase pending: do not confirm vendor popup");
Check(WorldDialogueRules.CanConfirm(true,WorldAutomationRules.Vendor,null,"兑换魔光键影宝箱？",3),"confirm only pending chest purchase");
Check(!WorldDialogueRules.CanConfirm(true,WorldAutomationRules.Guide,WorldAutomationRules.Vendor,"要前往键斗广场吗？",0),"changed NPC prevents popup confirmation");
Console.WriteLine("PASS: confirmation is scoped to owned NPC, prompt subject and pending purchase.");

Check(DependencyRules.Resolve(false,false)==DependencyStatus.NotInstalled,"uninstalled dependency");
Check(DependencyRules.Resolve(true,false)==DependencyStatus.Disabled,"installed unloaded dependency");
Check(DependencyRules.Resolve(true,true)==DependencyStatus.Enabled,"loaded dependency");
Check(DependencyRules.Missing(false,false,DependencyStatus.NotInstalled,DependencyStatus.NotInstalled)==null,"no external dependencies for typing and chest opening");
Check(DependencyRules.Missing(true,false,DependencyStatus.Enabled,DependencyStatus.NotInstalled)==null,"plaza navigation does not require teleport plugin");
Check(DependencyRules.Missing(true,true,DependencyStatus.Enabled,DependencyStatus.NotInstalled)!=null,"missing teleport blocks travel from other territory");
Check(DependencyRules.Missing(true,false,DependencyStatus.Disabled,DependencyStatus.Enabled)!=null,"disabled navigation blocks dependent feature");
Check(DependencyRules.Missing(true,true,DependencyStatus.Enabled,DependencyStatus.Enabled)==null,"dependencies enabled: feature available");
Check(!DependencyRules.NeedsTeleport(1360) && !DependencyRules.NeedsTeleport(144) && DependencyRules.NeedsTeleport(128),"teleport dependency is territory specific");
Check(!DependencyRules.ModeNeedsTravel(RunMode.Member,false) && !DependencyRules.ModeNeedsTravel(RunMode.Chests,false),"independent modes remain available");
Check(DependencyRules.ModeNeedsTravel(RunMode.Solo,false) && DependencyRules.ModeNeedsTravel(RunMode.Leader,false),"lobby solo and leader require navigation");
Check(!DependencyRules.ModeNeedsTravel(RunMode.Solo,true),"already inside: typing unaffected by missing travel plugins");
Console.WriteLine("PASS: installed/disabled/enabled dependency status, feature gating and territory-specific teleport requirements.");

Check(DependencyRules.CanRegisterWithoutTravel(true,false,true),"missing travel dependencies must not block nearby registration NPC");
Check(DependencyRules.CanRegisterWithoutTravel(true,true,false),"an open registration window requires no navigation");
Check(!DependencyRules.CanRegisterWithoutTravel(true,false,false),"no accessible NPC: wait for manual movement");
Check(!DependencyRules.CanRegisterWithoutTravel(false,false,true),"outside plaza: do not use unrelated NPCs");
Check(DependencyRules.ManualNpcTooltip=="缺少自动寻路功能，请手动前往npc","start hover wording");
Check(DependencyRules.ManualPlazaMessage=="缺少寻路组件，请手动前往键斗广场","waiting for manual plaza travel wording");
Console.WriteLine("PASS: solo/leader manual-NPC fallback remains usable without travel dependencies.");

// Without an imported IME vocabulary, known whole words still start directly in character mode.
var withoutImeList = new TrialMachine(dictionary,0,new Random(50),true,timing,imeDictionaryInstalled:false);
var forcedBuffer=""; var forcedKeys="";
for(long now=0;now<10000 && withoutImeList.CompletedCount<2;now+=10)
{
 var candidates=new Dictionary<int,string>();
 if(forcedBuffer=="j") { candidates[1]="计步"; candidates[2]="计"; }
 if(forcedBuffer=="计b") candidates[3]="步";
 var key=withoutImeList.Tick(new(true,forcedBuffer,[new($"/char/{withoutImeList.CompletedCount}/20","计，步！")],candidates),now);
 if(key==null)continue;
 forcedKeys+=key;
 if(key is >= 'a' and <= 'z')forcedBuffer+=key;
 else if(key=='2')forcedBuffer="计";
 else if(key=='3')forcedBuffer="计步";
 else if(key=='\n')forcedBuffer="";
}
Check(withoutImeList.CompletedCount==2 && forcedKeys=="j2b3\nj2b3\n","no IME list must bypass whole-word candidates and input each character: "+forcedKeys);
Check(withoutImeList.Fallbacks.Count==0,"known words forced to character mode must not be falsely recorded as missing");
Check(withoutImeList.ComposedEntries.Count==2,"character composition learning stays available");
var withImeList = new TrialMachine(dictionary,0,new Random(51),true,timing);
var installedBuffer="";var installedKeys="";
for(long now=0;now<3000 && withImeList.CompletedCount<1;now+=10)
{
 var candidates=installedBuffer=="j"?new Dictionary<int,string>{{1,"计步"},{2,"计"}}:new();
 var key=withImeList.Tick(new(true,installedBuffer,[new("/whole/20","计步")],candidates),now);
 if(key==null)continue;
 installedKeys+=key;
 if(key is >= 'a' and <= 'z')installedBuffer+=key;
 else if(key=='1')installedBuffer="计步";
 else if(key=='\n')installedBuffer="";
}
Check(withImeList.CompletedCount==1 && installedKeys=="j1\n","default installed setting must preserve early whole-word selection");
Console.WriteLine("PASS: imported IME vocabulary checkbox defaults to existing behavior; unchecked starts each word in strict character mode without whole-word retries.");

CastPriorityAuditTests.Run();
