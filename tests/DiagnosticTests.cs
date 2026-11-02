using AwakeLid;
using System.Diagnostics;
using System.Text.Json;

static partial class Tests
{
    static void DiagnosticTests()
    {
        // Leave fixtures in the ignored build output; never permanently delete user files.
        var root=Path.Combine(AppContext.BaseDirectory,"diagnostic-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var statusPath=Path.Combine(root,"status.json");
        var eventsPath=Path.Combine(root,"events.log");
        var clock=new TestClock();
        var diagnostics=new DiagnosticFiles(root,clock);
        const string original="{\"phase\":\"original\"}";
        const string active="{\"phase\":\"active\"}";
        const string expired="{\"phase\":\"expired\"}";
        Assert(diagnostics.PublishStatus(original),"initial status snapshot is published");
        var modified=File.GetLastWriteTimeUtc(statusPath);
        var power=new FakePower(); var state=new SavedState {Selection=3};
        var controller=new Coordinator(power,new FakeDisk(),state) {Log=message=>diagnostics.Log(message)};

        using(new LockedFiles(statusPath,eventsPath))
        {
            bool oldWriteFailed=false;
            try { File.WriteAllText(statusPath,active); }
            catch(IOException) { oldWriteFailed=true; }
            Assert(oldWriteFailed,"reproduce 0.1.1's direct status write failure under another process's exclusive lock");
            Assert(diagnostics.PublishStatus(original),"unchanged status needs no disk access even while the file is locked");
            controller.Tick(true);
            Assert(controller.IsActive&&state.Leases.Count==2&&power.Read(power.ActivePlan,Supply.Dc)==0,"locked log does not interrupt enabling both policies");
            Assert(!diagnostics.PublishStatus(active)&&diagnostics.StatusError.Length>0,"locked status is reported as a diagnostic warning");
            Assert(diagnostics.LogError.Length>0&&diagnostics.PendingLogEntries==2,"locked log retains bounded in-memory entries");
            for(int i=0;i<12;i++) { controller.Tick(true); diagnostics.PublishStatus(active); }
            Assert(controller.IsActive&&state.Leases.Count==2&&power.Writes==2,"status lock never pauses the session or toggles policies");
            // Manual edits must still win while diagnostics are unavailable.
            power.Values[(power.ActivePlan,Supply.Ac)]=3;
            controller.Tick(true);
            Assert(controller.Suppressed==1&&power.Read(power.ActivePlan,Supply.Ac)==3,"manual edit wins even when both diagnostic files are locked");
            controller.Tick(false);
            Assert(!controller.IsActive&&state.Leases.Count==0&&power.Read(power.ActivePlan,Supply.Ac)==3&&power.Read(power.ActivePlan,Supply.Dc)==2,"expiry restores DC and preserves edited AC despite status/log locks");
            Assert(!diagnostics.PublishStatus(expired),"retry backoff keeps the newest desired state in memory");
        }
        Assert(File.ReadAllText(statusPath)==original&&File.GetLastWriteTimeUtc(statusPath)==modified,"failed publish preserves the complete previous snapshot");
        Assert(!diagnostics.PublishStatus(expired),"unlocking does not bypass the bounded retry delay");
        clock.Advance(TimeSpan.FromSeconds(5));
        diagnostics.FlushLogs();
        Assert(diagnostics.PublishStatus(expired),"latest status automatically publishes after unlock and retry delay");
        Assert(diagnostics.Warning.Length==0&&diagnostics.PendingLogEntries==0,"transient diagnostic warnings clear after successful retry");
        Assert(File.ReadAllText(eventsPath).Contains("已恢复")&&File.ReadAllText(eventsPath).Contains("保留外部修改"),"queued recovery and manual-edit logs are flushed after unlock");
        Assert(JsonDocument.Parse(File.ReadAllText(statusPath)).RootElement.GetProperty("phase").GetString()=="expired","published JSON reflects the latest state, not an intermediate snapshot");

        using(var reader=new FileStream(statusPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
        {
            Assert(diagnostics.PublishStatus(active),"a replace-compatible reader can remain open during publication: "+diagnostics.StatusError);
            using var prior=JsonDocument.Parse(reader);
            using var current=JsonDocument.Parse(File.ReadAllText(statusPath));
            Assert(prior.RootElement.GetProperty("phase").GetString()=="expired"&&current.RootElement.GetProperty("phase").GetString()=="active","existing readers see a complete old snapshot and new readers see a complete new snapshot");
        }
        using(new LockedFiles(statusPath+".new"))
            Assert(!diagnostics.PublishStatus(expired),"a locked staging file also remains a nonfatal diagnostic error");
        Assert(File.ReadAllText(statusPath)==active,"staging failure leaves the published snapshot untouched");
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert(diagnostics.PublishStatus(expired)&&diagnostics.StatusError.Length==0,"staging-file contention recovers on retry");

        // A directory where a file is expected yields UnauthorizedAccessException on Windows.
        var denied=Path.Combine(root,"unwritable");
        Directory.CreateDirectory(Path.Combine(denied,"status.json.new"));
        Directory.CreateDirectory(Path.Combine(denied,"events.log"));
        var deniedDiagnostics=new DiagnosticFiles(denied,clock);
        Assert(!deniedDiagnostics.PublishStatus(active)&&deniedDiagnostics.StatusError.Length>0,"unwritable diagnostics do not throw into the guardian");
        deniedDiagnostics.Log("unwritable log");
        Assert(deniedDiagnostics.LogError.Length>0&&deniedDiagnostics.PendingLogEntries==1,"log access failures are visible without interrupting recovery");
        Parallel.For(0,255,i=>deniedDiagnostics.Log("concurrent-"+i));
        Assert(deniedDiagnostics.PendingLogEntries==128&&deniedDiagnostics.DroppedLogEntries==128,"concurrent diagnostic writers preserve the queue bound without throwing");

        var bounded=Path.Combine(root,"bounded"); Directory.CreateDirectory(bounded);
        var boundedDiagnostics=new DiagnosticFiles(bounded,clock);
        using(new LockedFiles(Path.Combine(bounded,"error.log")))
        {
            for(int i=0;i<130;i++) boundedDiagnostics.Log("entry-"+i,error:true);
            Assert(boundedDiagnostics.PendingLogEntries==128&&boundedDiagnostics.DroppedLogEntries==2,"persistent locks have bounded memory use and report dropped entries");
        }
        clock.Advance(TimeSpan.FromSeconds(5)); boundedDiagnostics.FlushLogs();
        Assert(boundedDiagnostics.PendingLogEntries==0&&boundedDiagnostics.LogError.Length==0&&boundedDiagnostics.Warning.Length>0,"log output resumes while dropped-entry warning stays visible");

        // Unlike diagnostic files, state.json is mandatory before any policy override.
        var journalPath=Path.Combine(root,"state.json");
        var journal=new JsonStateStore(journalPath);
        var durable=new SavedState {Selection=1}; journal.Save(durable);
        var guardedPower=new FakePower(); var guarded=new Coordinator(guardedPower,journal,durable);
        using(new LockedFiles(journalPath))
        {
            bool blocked=false;
            try { guarded.Tick(true); }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException) { blocked=true; }
            Assert(blocked&&guardedPower.Writes==0,"locked recovery journal still prevents all policy writes");
        }
        Assert(journal.Load().Leases.Count==0,"failed journal replacement preserves its previous contents");
        guarded.Recover();
        Assert(durable.Leases.Count==0&&guardedPower.Writes==0,"journal failure recovery does not change power settings");
    }

    sealed class TestClock : TimeProvider
    {
        DateTimeOffset current=new(2026,11,2,0,0,0,TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow()=>current;
        public void Advance(TimeSpan value)=>current+=value;
    }
    sealed class LockedFiles : IDisposable
    {
        readonly Process process;
        public LockedFiles(params string[] paths)
        {
            var exe=Environment.ProcessPath!;
            var start=new ProcessStartInfo(exe) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
            if(exe.EndsWith("dotnet.exe",StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(Tests).Assembly.Location);
            start.ArgumentList.Add("--lock-files");
            foreach(var path in paths) start.ArgumentList.Add(path);
            process=Process.Start(start)!;
            try
            {
                if(process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult()!="LOCKED")
                    throw new Exception("lock fixture did not start: "+process.StandardError.ReadToEnd());
            }
            catch { if(!process.HasExited) process.Kill(); process.Dispose(); throw; }
        }
        public void Dispose()
        {
            if(!process.HasExited)
            {
                process.StandardInput.WriteLine("release"); process.StandardInput.Flush();
                if(!process.WaitForExit(3000)) { process.Kill(); process.WaitForExit(); }
            }
            process.Dispose();
        }
    }
}
