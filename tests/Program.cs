using AwakeLid;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

static partial class Tests
{
    static int count;
    static void Assert(bool condition,string message) { if(!condition) throw new Exception(message); count++; }
    static void Main(string[] args)
    {
        if(args.Length>0 && args[0]=="--lock-files")
        {
            var handles=new List<FileStream>();
            try
            {
                foreach(var path in args.Skip(1)) handles.Add(new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None));
                Console.WriteLine("LOCKED"); Console.Out.Flush(); Console.ReadLine();
            }
            finally { foreach(var handle in handles) handle.Dispose(); }
            return;
        }
        if(args.Length>0 && args[0]=="--crash-client")
        {
            using var session=new HookSession(new IntPtr(long.Parse(args[1])),args[2],true);
            session.Update(1,2,true);
            Console.WriteLine("READY"); Console.Out.Flush(); Thread.Sleep(Timeout.Infinite); return;
        }
        DiagnosticTests();
        if(args.Contains("--diagnostics-only")) { Console.WriteLine($"PASS: {count} diagnostic/policy assertions; no real power writes."); return; }
        CoreTests(!args.Contains("--skip-real-power"));
        var paths=args.Where(a=>a!="--skip-real-power").ToArray();
        if(paths.Length>0) NativeTests(Path.GetFullPath(paths[0]),Path.GetFullPath(paths[1]));
        Console.WriteLine($"PASS: {count} assertions; real Windows power settings were read only.");
    }
    static void CoreTests(bool probeReal)
    {
        var p=new FakePower(); var s=new SavedState {Selection=3}; var disk=new FakeDisk();
        var c=new Coordinator(p,disk,s);
        p.BeforeWrite=()=>Assert(disk.Last?.Leases.Count>0,"restore journal must be durable before write");
        c.Tick(true); Assert(p.Read(p.ActivePlan,Supply.Ac)==0&&p.Read(p.ActivePlan,Supply.Dc)==0,"both supplies enabled");
        c.Tick(true); Assert(s.Leases.Count==2&&s.Leases[0].Original==1,"repeated tick preserves original baseline");
        c.Tick(false); Assert(p.Read(p.ActivePlan,Supply.Ac)==1&&p.Read(p.ActivePlan,Supply.Dc)==2,"expiry restores distinct originals");
        Assert(s.Leases.Count==0,"journal released only after restore");
        c.Tick(true); p.Values[(p.ActivePlan,Supply.Ac)]=3; c.Tick(true);
        Assert(p.Read(p.ActivePlan,Supply.Ac)==3&&c.Suppressed==1,"manual edit must suspend AC without fighting user");
        c.Tick(false); Assert(p.Read(p.ActivePlan,Supply.Ac)==3&&p.Read(p.ActivePlan,Supply.Dc)==2,"expiry preserves manual edit and restores DC");
        c.Tick(true); c.Select(0); c.Tick(true);
        Assert(p.Read(p.ActivePlan,Supply.Ac)==3&&p.Read(p.ActivePlan,Supply.Dc)==2,"system option restores both and clears selection");
        Assert(s.Selection==0&&s.Leases.Count==0,"system mode no remaining leases");
        c.Select(1); c.Tick(true); c.Select(3); c.Tick(true); c.Select(2); c.Tick(true);
        Assert(p.Read(p.ActivePlan,Supply.Ac)==3&&p.Read(p.ActivePlan,Supply.Dc)==0,"independent supply toggles");
        var oldPlan=p.ActivePlan; p.ActivePlan=Guid.NewGuid(); p.Init(p.ActivePlan,2,1); c.Tick(true);
        Assert(p.Read(oldPlan,Supply.Dc)==2&&p.Read(p.ActivePlan,Supply.Dc)==0,"plan switch restores old plan before leasing new plan");
        var loaded=JsonSerializer.Deserialize<SavedState>(JsonSerializer.Serialize(s))!;
        new Coordinator(p,new FakeDisk(),loaded).Recover();
        Assert(p.Read(p.ActivePlan,Supply.Dc)==1&&loaded.Leases.Count==0,"crash recovery uses persisted baseline");
        s=new(){Selection=1}; disk=new(); c=new(p,disk,s); p.FailNext=true;
        try { c.Tick(true); Assert(false,"write failure was not reported"); } catch(IOException) { }
        Assert(s.Leases.Count==1,"failed write retains prepared journal for recovery");
        c.Recover(); Assert(s.Leases.Count==0,"failed write recovery does not force policy");
        var failDisk=new FakeDisk {Fail=true}; s=new(){Selection=1}; c=new(p,failDisk,s);
        int before=p.Writes;
        try { c.Tick(true); Assert(false,"journal failure was not reported"); } catch(IOException) { }
        Assert(p.Writes==before,"journal failure prevents all policy writes");
        p.BeforeWrite=null;
        // Built-in APIs: a real read only probe, no Windows writes.
        if(probeReal)
        {
            var real=new WindowsPowerStore(); var actualPlan=real.ActivePlan;
            Assert(real.Read(actualPlan,Supply.Ac)<=3&&real.Read(actualPlan,Supply.Dc)<=3,"real lid policy readable");
        }
    }
    static void NativeTests(string hostPath,string dll)
    {
        using var host=Start(hostPath,"");
        var line=host.StandardOutput.ReadLine()!.Split(' '); var hwnd=new IntPtr(long.Parse(line[1]));
        try
        {
            using(var session=new HookSession(hwnd,dll,true))
            {
                Assert(session.Connected,"remote hidden host confirms subclass installed"); session.Update(3,2,true);
                var root=Send(hwnd,0x8000+100);
                Send(hwnd,0x211,new IntPtr(1)); Send(hwnd,0x117,root);
                Assert(GetMenuItemCount(root)==4,"original root plus separator and extension");
                var sub=GetSubMenu(root,3);
                Assert(Text(root,3)=="合盖保持唤醒","exact root label");
                Assert(Text(sub,0)=="跟随 Awake"&&Text(sub,1).Trim()=="接通电源时"&&Text(sub,2).Trim()=="使用电池时","group title and two choices");
                Assert((GetMenuState(sub,1,0x400)&8)!=0&&(GetMenuState(sub,2,0x400)&8)!=0,"both may be checked");
                Assert((GetMenuState(sub,1,0x400)&3)==0&&(GetMenuState(sub,2,0x400)&3)==0,"Awake on enables both choices");
                Assert((GetMenuState(sub,3,0x400)&0x800)!=0&&Text(sub,4)=="跟随系统设置","separator before system mode");
                uint ac=GetMenuItemID(sub,1), system=GetMenuItemID(sub,4);
                Send(hwnd,0x212,new IntPtr(1));
                Assert(GetMenuItemCount(root)==2,"temporary items removed when menu closes");
                Send(hwnd,0x111,new IntPtr(ac)); Assert(session.ReadCommand()==258,"AC click dispatched to guardian");
                Send(hwnd,0x111,new IntPtr(system)); Assert(session.ReadCommand()==256,"system click dispatched to guardian");
                Send(hwnd,0x111,new IntPtr(1001)); Assert(Send(hwnd,0x8000+102).ToInt32()==1,"original Awake commands remain untouched");
                // Awake can destroy/rebuild its menu repeatedly.
                for(int i=0;i<4;i++) { root=Send(hwnd,0x8000+100); Send(hwnd,0x211,new IntPtr(1)); Send(hwnd,0x117,root); Assert(GetMenuItemCount(root)==4,"rebuilt menu extended exactly once"); Send(hwnd,0x212,new IntPtr(1)); }
                session.Update(0,0,false); root=Send(hwnd,0x8000+100); Send(hwnd,0x211,new IntPtr(1)); Send(hwnd,0x117,root); sub=GetSubMenu(root,3);
                Assert((GetMenuState(sub,1,0x400)&8)==0&&(GetMenuState(sub,4,0x400)&8)!=0,"system mode mutually exclusive");
                Assert((GetMenuState(sub,1,0x400)&3)!=0&&(GetMenuState(sub,2,0x400)&3)!=0,"Awake off grays both follow choices");
                Assert((GetMenuState(sub,4,0x400)&3)==0,"system mode stays usable when Awake is off");
                uint dc=GetMenuItemID(sub,2);
                Send(hwnd,0x212,new IntPtr(1));
                Send(hwnd,0x111,new IntPtr(ac)); Send(hwnd,0x111,new IntPtr(dc));
                Assert(session.ReadCommand()==0,"disabled choices cannot dispatch even if WM_COMMAND is sent directly");
                Send(hwnd,0x111,new IntPtr(system)); Assert(session.ReadCommand()==256,"system option still dispatches while Awake is off");
                session.Update(0,0,true); root=Send(hwnd,0x8000+100); Send(hwnd,0x211,new IntPtr(1)); Send(hwnd,0x117,root); sub=GetSubMenu(root,3);
                Assert((GetMenuState(sub,1,0x400)&3)==0&&(GetMenuState(sub,2,0x400)&3)==0,"reopening menu after Awake starts re-enables choices");
                Send(hwnd,0x212,new IntPtr(1));
            }
            var unloadDeadline=DateTime.UtcNow.AddSeconds(4);
            while(Send(hwnd,0x8000+104)!=IntPtr.Zero && DateTime.UtcNow<unloadDeadline) { Send(hwnd,0); Thread.Sleep(100); }
            Assert(Send(hwnd,0x8000+104)==IntPtr.Zero,"explicit unload releases the DLL, not just the subclass");
            var exe=Environment.ProcessPath!;
            var recoveryMarker=Path.Combine(Path.GetDirectoryName(dll)!,"orphan-recovery-test.txt");
            File.WriteAllText(recoveryMarker,"NOT_CALLED");
            var args=exe.EndsWith("dotnet.exe",StringComparison.OrdinalIgnoreCase)?$"\"{typeof(Tests).Assembly.Location}\" --crash-client {hwnd.ToInt64()} \"{dll}\"":$"--crash-client {hwnd.ToInt64()} \"{dll}\"";
            using var child=Start(exe,args);
            Assert(child.StandardOutput.ReadLine()=="READY","crash-client installed hook");
            child.Kill(); child.WaitForExit(); Thread.Sleep(2200);
            Assert(Send(hwnd,0x8000+104)==IntPtr.Zero,"guardian crash self-detaches and unloads native callbacks");
            Assert(File.ReadAllText(recoveryMarker)=="RECOVERY_CALLED","guardian crash triggers an independent recovery process");
            var rootAfterCrash=Send(hwnd,0x8000+100); Send(hwnd,0x211,new IntPtr(1)); Send(hwnd,0x117,rootAfterCrash);
            Assert(GetMenuItemCount(rootAfterCrash)==2,"after guardian crash original menu remains intact");
        }
        finally { Send(hwnd,0x8000+103); if(!host.WaitForExit(3000)) host.Kill(); }
        using var incompatible=Start(hostPath,"bad-class");
        var invalidWindow=new IntPtr(long.Parse(incompatible.StandardOutput.ReadLine()!.Split(' ')[1]));
        try
        {
            bool failed=false;
            try { using var bad=new HookSession(invalidWindow,dll,true); } catch(InvalidOperationException) { failed=true; }
            Assert(failed,"incompatible window must fail handshake");
        }
        finally { Send(invalidWindow,0x8000+103); incompatible.WaitForExit(3000); }
    }
    static Process Start(string exe,string args)=>Process.Start(new ProcessStartInfo(exe,args) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true })!;
    static IntPtr Send(IntPtr hwnd,uint message,IntPtr wp=default)
    {
        if(Native.SendMessageTimeoutW(hwnd,message,wp,IntPtr.Zero,2,3000,out var result)==IntPtr.Zero) throw new Exception("mock message timed out");
        return new IntPtr(unchecked((long)result.ToUInt64()));
    }
    static string Text(IntPtr menu,int position) { var text=new StringBuilder(256); GetMenuStringW(menu,(uint)position,text,256,0x400); return text.ToString(); }
    [DllImport("user32.dll")] static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll")] static extern IntPtr GetSubMenu(IntPtr menu,int position);
    [DllImport("user32.dll")] static extern uint GetMenuItemID(IntPtr menu,int position);
    [DllImport("user32.dll")] static extern uint GetMenuState(IntPtr menu,uint item,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetMenuStringW(IntPtr menu,uint item,StringBuilder text,int max,uint flags);
    sealed class FakePower : IPowerStore
    {
        public Guid ActivePlan {get;set;}=Guid.NewGuid();
        public Dictionary<(Guid,Supply),uint> Values=[];
        public int Writes; public bool FailNext; public Action? BeforeWrite;
        public FakePower()=>Init(ActivePlan,1,2);
        public void Init(Guid plan,uint ac,uint dc) {Values[(plan,Supply.Ac)]=ac;Values[(plan,Supply.Dc)]=dc;}
        public uint Read(Guid plan,Supply supply)=>Values[(plan,supply)];
        public void Write(Guid plan,Supply supply,uint value) { BeforeWrite?.Invoke(); Writes++; if(FailNext){FailNext=false;throw new IOException("injected write failure");} Values[(plan,supply)]=value; }
    }
    sealed class FakeDisk : IStateStore
    {
        public SavedState? Last; public bool Fail;
        public void Save(SavedState state) { if(Fail)throw new IOException("injected journal failure"); Last=JsonSerializer.Deserialize<SavedState>(JsonSerializer.Serialize(state)); }
    }
}
