using MakoMaster;
internal static class ChestOpeningTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static void Run()
    {
        var finite=new ChestOpeningSession(2);
        Check(finite.Tick(true,5,true,0),"finite session did not request first use"); finite.ActionSent(0);
        Check(!finite.Tick(true,5,true,250) && finite.Opened==0,"dispatch counted as an opened box or repeated before feedback");
        Check(!finite.Tick(true,4,true,500) && finite.Opened==1,"inventory decrease not confirmed");
        Check(!finite.Tick(true,4,true,1499),"request ignored consumption cooldown");
        Check(finite.Tick(true,4,true,1500),"second use missing"); finite.ActionSent(1500);
        Check(!finite.Tick(true,3,true,1750) && !finite.Running && finite.Opened==2,"finite limit did not stop exactly at two");
        Check(!finite.Tick(true,3,true,99999),"finished session requested more boxes");
        foreach(var limit in new[]{0,10})
        {
            var session=new ChestOpeningSession(limit);var count=3;long? pendingAt=null;var requests=0;
            for(long time=0;time<20000 && session.Running;time+=250)
            {
                if(pendingAt!=null && time-pendingAt>=250){count--;pendingAt=null;}
                if(session.Tick(true,count,true,time)){requests++;session.ActionSent(time);pendingAt=time;}
            }
            Check(!session.Running && session.Opened==3 && count==0 && requests==3,"unlimited/depleted finite session did not stop on empty inventory");
        }
        var empty=new ChestOpeningSession(0);
        Check(!empty.Tick(false,0,false,0) && empty.Running,"unloaded inventory mistaken for empty inventory");
        Check(!empty.Tick(true,0,true,100) && !empty.Running,"empty inventory did not stop");
        var failed=new ChestOpeningSession(0);
        Check(failed.Tick(true,2,true,0),"failed-use setup");failed.ActionSent(0);
        Check(!failed.Tick(true,2,true,7999) && failed.Running && failed.Opened==0,"pending use repeated or counted prematurely");
        Check(!failed.Tick(true,2,true,8000) && !failed.Running && failed.Opened==0,"unconfirmed use retried instead of stopping");
        var unavailable=new ChestOpeningSession(1);
        Check(!unavailable.Tick(true,5,false,0) && unavailable.Running,"blocked use was dispatched");
        unavailable.Stop("UI");Check(!unavailable.Tick(true,5,true,9000),"manual stop still used an item");
        var changed=new ChestOpeningSession(0);changed.Tick(true,5,true,0);changed.ActionSent(0);
        Check(!changed.Tick(true,3,true,500) && !changed.Running && changed.Opened==0,"extra inventory change counted as confirmed box openings");
        var rejected=false;try{_ = new ChestOpeningSession(-1);}catch(ArgumentOutOfRangeException){rejected=true;}
        Check(rejected,"negative count accepted");
        Console.WriteLine("PASS: finite chest limit; zero opens until empty; count only confirmed consumption; no duplicate pending use; timeout/extra-change stop; unloaded inventory and manual stop.");
    }
}
