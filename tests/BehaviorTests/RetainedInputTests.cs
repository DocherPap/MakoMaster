using MakoMaster;

internal static class RetainedInputTests
{
    private static void Check(bool condition,string message) { if(!condition)throw new Exception(message); }
    internal static void Run()
    {
        var entry=new DictionaryEntry("计步",["ji","bu"],"jibu","ji'bu");
        var bank=new Dictionary<string,DictionaryEntry>{{"计步",entry}};
        var timing=new TimingSettings(100,100,300,300);
        foreach(var inferred in new[]{false,true})
        {
            var source=inferred ? new Dictionary<string,DictionaryEntry>
            {
                ["计数"]=new("计数",["ji","shu"],"jishu","ji'shu"),
                ["步行"]=new("步行",["bu","xing"],"buxing","bu'xing")
            } : bank;
            var machine=new TrialMachine(source,0,new Random(1),true,timing);
            var buffer="";var keys="";var page=1;var turns=0;long lastKeyAt=-100;
            var selectedFirst=false;
            for(long now=0;now<30000 && machine.CompletedCount==0;now+=10)
            {
                var choices=new Dictionary<int,string>{{1,"错误"}};
                if(buffer=="jibu" && page==6)choices[2]="计";
                if(buffer=="计bu" && page==5)choices[3]="步";
                var key=machine.Tick(new(true,buffer,[new("/word","计，步！")],choices,$"({page}/9)",9),now);
                if(key==null)continue;
                Check(now-lastKeyAt>=100,"retained paging or selection bypassed key interval");lastKeyAt=now;
                keys+=key;
                if(key is >= 'a' and <= 'z')buffer+=key;
                else if(key=='\f'){page++;turns++;}
                else if(key=='2'){buffer="计bu";page=1;selectedFirst=true;Check(machine.ComposedEntries.Count==0,"partial word was learned");}
                else if(key=='3'){buffer="计步";page=1;}
                else if(key=='\n')buffer="";
                else if(key=='\b')throw new Exception("candidate miss cleared and retyped input");
            }
            Check(selectedFirst && turns==9 && machine.CompletedCount==1,"did not page beyond three turns for each retained character: "+machine.Result);
            Check(keys=="jibu"+new string('\f',5)+"2"+new string('\f',4)+"3\n","pinyin was not preserved throughout selection: "+keys);
            Check(machine.ComposedEntries.Count==1 && machine.ComposedEntries.Peek().Pinyin=="jibu","completed composition not queued for learning");
        }

        // Single-character candidates cannot truncate typing before the whole spelling is entered.
        var earlySingle=new TrialMachine(bank,0,new Random(1),true,timing);
        var typed="";var emitted="";
        for(long t=0;t<5000 && earlySingle.CompletedCount==0;t+=10)
        {
            var choices=typed.StartsWith("计")?new Dictionary<int,string>{{2,"步"}}:new Dictionary<int,string>{{1,"计"}};
            var key=earlySingle.Tick(new(true,typed,[new("/word","计步")],choices),t);
            if(key==null)continue;emitted+=key;
            if(key is >= 'a' and <= 'z')typed+=key;
            else if(key=='1'){Check(typed=="jibu","single selected before finishing pinyin");typed="计bu";}
            else if(key=='2')typed="计步";
            else if(key=='\n')typed="";
        }
        Check(emitted=="jibu12\n","unexpected keys in early single selection: "+emitted);

        // A complete remaining suffix can still be selected after committing the first character.
        var longEntry=new DictionaryEntry("计步行",["ji","bu","xing"],"jibuxing","ji'bu'xing");
        var suffix=new TrialMachine(new Dictionary<string,DictionaryEntry>{{longEntry.Word,longEntry}},0,new Random(1),true,timing);
        typed="";emitted="";
        for(long t=0;t<6000 && suffix.CompletedCount==0;t+=10)
        {
            var choices=typed=="jibuxing"?new Dictionary<int,string>{{1,"计"}}
                :typed=="计buxing"?new Dictionary<int,string>{{2,"步行"}}:new();
            var key=suffix.Tick(new(true,typed,[new("/word","计步行")],choices),t);
            if(key==null)continue;emitted+=key;
            if(key is >= 'a' and <= 'z')typed+=key;
            else if(key=='1')typed="计buxing";
            else if(key=='2')typed="计步行";
            else if(key=='\n')typed="";
        }
        Check(emitted=="jibuxing12\n" && suffix.CompletedCount==1,"remaining suffix selection failed");

        // A teammate removing an enemy during the first character selection sends once, without teaching an unfinished word.
        var vanished=new TrialMachine(bank,0,new Random(1),true,timing);
        typed="";var chose=false;
        for(long t=0;t<5000 && vanished.CompletedCount==0;t+=10)
        {
            var choices=typed=="jibu"?new Dictionary<int,string>{{1,"计"}}:new();
            var key=vanished.Tick(new(true,typed,chose?[]:[new("/word","计步")],choices),t);
            if(key is >= 'a' and <= 'z')typed+=key;
            else if(key=='1'){typed="";chose=true;}
        }
        Check(vanished.CompletedCount==1 && vanished.ComposedEntries.Count==0,"unfinished vanished target was taught or retried");

        // An unchanged/end page preserves composition instead of switching to retyping.
        var endPage=new TrialMachine(bank,0,new Random(1),true,timing);
        typed="";emitted="";
        for(long t=0;t<12000;t+=10)
        {
            var key=endPage.Tick(new(true,typed,[new("/word","计步")],new Dictionary<int,string>{{1,"错误"}},"(1/1)",1),t);
            if(key==null)continue;emitted+=key;
            if(key is >= 'a' and <= 'z')typed+=key;
        }
        Check(typed=="jibu" && emitted.Count(c=>c=='\f')>3 && !emitted.Contains('\b'),"end-page guard discarded composition");
        endPage.Stop("UI");
        Check(endPage.Tick(new(true,typed,[new("/word","计步")],new Dictionary<int,string>()),20000)==null,"manual stop left paging active");
        Console.WriteLine("PASS: whole pinyin retained past three pages, character and suffix commits preserve remaining pinyin, no retyping, inferred words, unchanged-page wait, timing and UI stop.");
    }
}
