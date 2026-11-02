using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace AwakeLid;
internal static class Program
{
    internal static readonly string Home = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    internal static readonly string Data = Path.Combine(Home, "data");
    internal static readonly DiagnosticFiles Diagnostics = new(Data);
    internal static readonly string Version = typeof(Program).Assembly.GetName().Version!.ToString(3);
    internal static readonly string PipeName = "AwakeLid.v1." + Environment.UserName;
    internal static readonly string StartupLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "AwakeLid.lnk");
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if(args.Contains("--recover-once")) return RecoverOnce();
        if(args.Contains("--probe"))
        {
            try
            {
                var window=Native.FindAwake();
                if(window==IntPtr.Zero) throw new InvalidOperationException("Awake 未运行。");
                string result;
                using(var probe=new HookSession(window,Path.Combine(Home,"AwakeLidHook.dll")))
                    result=JsonSerializer.Serialize(new {connected=probe.Connected,version=probe.Version,pid=probe.Pid,powerToysManaged=Native.IsPowerToysChild(probe.Pid),powerWrites=0});
                File.WriteAllText(Path.Combine(Home,"probe.json"),result); Console.WriteLine(result); return 0;
            }
            catch(Exception error) { File.WriteAllText(Path.Combine(Home,"probe.json"),JsonSerializer.Serialize(new {error=error.Message})); return 1; }
        }
        if (args.Contains("--install-startup"))
        {
            try { InstallStartup(); return 0; }
            catch(Exception error) { MessageBox.Show(error.Message,"开机启动设置失败"); return 1; }
        }
        string? command = args.Contains("--stop") ? "stop" : args.Contains("--status") ? "status" : args.Contains("--show") ? "show" : null;
        if (command != null)
        {
            var response = Send(command);
            if (command == "status") { File.WriteAllText(Path.Combine(Home,"状态.json"), response); Console.WriteLine(response); }
            else if (response.StartsWith("ERROR")) File.WriteAllText(Path.Combine(Home,"操作结果.txt"), response);
            return response.StartsWith("ERROR") ? 1 : 0;
        }
        using var mutex = new Mutex(true, "Local\\" + PipeName, out bool first);
        if (!first) { Send("show"); return 0; }
        Directory.CreateDirectory(Data);
        try
        {
            var context = new Guardian();
            Application.Run(context);
            return context.ExitCode;
        }
        catch (Exception error)
        {
            Diagnostics.Log(error.ToString(), error: true);
            MessageBox.Show("外挂启动失败；请保留 data 目录中的恢复记录。\n" + error.Message, "Awake 合盖扩展");
            return 1;
        }
    }
    internal static string Send(string command)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(2000);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen:true) { AutoFlush=true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen:true);
            writer.WriteLine(command);
            return reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(12)).GetAwaiter().GetResult() ?? "ERROR: 后台助手没有返回结果。";
        }
        catch (TimeoutException) { return command == "stop" ? RecoverOffline() : "ERROR: 外挂未运行。"; }
        catch (IOException error) { return "ERROR: " + error.Message; }
    }
    static int RecoverOnce()
    {
        using var mutex=new Mutex(false,"Local\\"+PipeName);
        bool owned=false;
        try
        {
            try { owned=mutex.WaitOne(0); } catch(AbandonedMutexException) { owned=true; }
            if(!owned) return 0; // A live guardian owns the journal; never compete with it.
            Directory.CreateDirectory(Data);
            var persistence=new JsonStateStore(Path.Combine(Data,"state.json"));
            var engine=new Coordinator(new WindowsPowerStore(),persistence,persistence.Load());
            engine.Log=message=>Diagnostics.Log("孤立会话恢复："+message);
            engine.Recover();
            return 0;
        }
        catch(Exception error)
        {
            Diagnostics.Log("孤立会话恢复失败："+error, error: true);
            return 1;
        }
        finally { if(owned) mutex.ReleaseMutex(); }
    }
    static string RecoverOffline()
    {
        try
        {
            using var mutex = new Mutex(true, "Local\\" + PipeName, out bool first);
            if (!first) return "ERROR: 后台助手仍在运行但未响应；请稍后重试，不要删除恢复记录。";
            var store = new JsonStateStore(Path.Combine(Data,"state.json"));
            var engine = new Coordinator(new WindowsPowerStore(), store, store.Load());
            engine.Stop();
            return "OK: 未运行；遗留合盖设置已检查并恢复。";
        }
        catch (Exception error) { return "ERROR: 恢复未完成：" + error.Message; }
    }
    static void InstallStartup()
    {
        // Explicit command only. Nothing is registered by the normal launcher.
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows 快捷方式服务不可用。");
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic link = shell.CreateShortcut(StartupLink);
        if(File.Exists(StartupLink) && !string.Equals(Path.GetFullPath((string)link.TargetPath),Path.Combine(Home,"AwakeLid.exe"),StringComparison.OrdinalIgnoreCase))
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            throw new InvalidOperationException("已有其他目录的同名启动项。请先卸载旧包，避免覆盖已有配置。");
        }
        link.TargetPath = Path.Combine(Home,"AwakeLid.exe"); link.WorkingDirectory=Home;
        link.Description="Awake 合盖菜单扩展"; link.Save();
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        MessageBox.Show("已设置当前用户登录时启动。", "Awake 合盖扩展");
    }
}

internal sealed class Guardian : ApplicationContext
{
    readonly Control dispatcher = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval=500 };
    readonly JsonStateStore store;
    readonly Coordinator engine;
    readonly NotifyIcon notification = new() { Icon=System.Drawing.SystemIcons.Warning, Text="Awake 合盖扩展" };
    readonly HashSet<string> notices = [];
    readonly CancellationTokenSource cancellation = new();
    HookSession? hook;
    DateTime nextAttach, unknownSince, missingWindowSince;
    string status="正在初始化", awakeVersion="未运行", lastError="";
    bool closing, faulted, awakeEnabled;
    public int ExitCode { get; private set; }
    public Guardian()
    {
        dispatcher.CreateControl();
        store = new(Path.Combine(Program.Data,"state.json"));
        engine = new(new WindowsPowerStore(),store,store.Load()) { Log=Log };
        engine.Recover(); // Recover an interrupted previous session before taking any new ownership.
        SystemEvents.SessionEnding += SessionEnding;
        timer.Tick += (_,_) => Tick(); timer.Start();
        _=Listen();
        Tick();
    }
    void Log(string message) => Program.Diagnostics.Log(message);
    void Warn(string key,string text)
    {
        if (!notices.Add(key)) return;
        lastError=text; Log(text);
        notification.Visible=true;
        notification.ShowBalloonTip(12000,"Awake 合盖扩展",text,ToolTipIcon.Warning);
        _=Task.Delay(15000).ContinueWith(_ => { if (!closing) dispatcher.BeginInvoke(() => notification.Visible=false); });
    }
    bool? ReadAwakeState()
    {
        var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Microsoft","PowerToys","Awake","settings.json");
        try
        {
            using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            using var json=JsonDocument.Parse(input);
            int mode=json.RootElement.GetProperty("properties").GetProperty("mode").GetInt32();
            if (mode is <0 or >3) throw new InvalidDataException("不支持的 Awake 模式。");
            unknownSince=default;
            return mode!=0;
        }
        catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException or InvalidDataException)
        {
            if (unknownSince==default) unknownSince=DateTime.UtcNow;
            if ((DateTime.UtcNow-unknownSince).TotalSeconds>3) Warn("awake-config", "无法读取 Awake 状态；已暂停联动并检查恢复。"+error.Message);
            return null;
        }
    }
    void Tick()
    {
        if (closing) return;
        try
        {
            var window=Native.FindAwake();
            var awakeProcesses=Process.GetProcessesByName("PowerToys.Awake");
            bool awakeProcessExists=awakeProcesses.Length>0;
            foreach(var awakeProcess in awakeProcesses) awakeProcess.Dispose();
            if(window==IntPtr.Zero && awakeProcessExists)
            {
                if(missingWindowSince==default) missingWindowSince=DateTime.UtcNow;
                if((DateTime.UtcNow-missingWindowSince).TotalSeconds>15) Warn("window-missing","Awake 正在运行，但无法识别其托盘窗口。可能存在版本兼容问题；合盖联动未启用。");
            }
            else missingWindowSince=default;
            if(window!=IntPtr.Zero)
            {
                Native.GetWindowThreadProcessId(window,out uint ownerPid);
                if(!Native.IsPowerToysChild((int)ownerPid))
                {
                    Warn("standalone","仅支持由 PowerToys 主程序管理的 Awake；独立命令行实例不会接管合盖设置。");
                    window=IntPtr.Zero;
                }
            }
            if (hook!=null && (window!=hook.Window || !hook.Connected))
            {
                bool unexpected=window==hook.Window;
                engine.Recover(); hook.Dispose(); hook=null;
                if (unexpected) Warn("hook-lost","Awake 菜单扩展失去响应，已恢复仍由外挂管理的合盖设置。请查看状态。");
            }
            if (window!=IntPtr.Zero && hook==null && DateTime.UtcNow>=nextAttach)
            {
                nextAttach=DateTime.UtcNow.AddSeconds(10);
                hook=new HookSession(window,Path.Combine(Program.Home,"AwakeLidHook.dll"));
                awakeVersion=hook.Version; Log("Hook 已加载；Awake "+awakeVersion);
                if (awakeVersion!="0.101.2362.0") Warn("version-"+awakeVersion,"检测到尚未验证的 Awake 版本 "+awakeVersion+"。请右键原托盘图标验证合盖菜单；验证前暂不接管合盖设置。");
            }
            var awake=hook==null ? false : ReadAwakeState();
            bool compatible=hook!=null && (awakeVersion=="0.101.2362.0" || hook.Menus>0);
            awakeEnabled=awake==true && compatible && !faulted;
            if (hook!=null)
            {
                var command=hook.ReadCommand();
                if(command is >=256 and <=259)
                {
                    var mask=command-256;
                    // Check authoritative Awake state again on dispatch; do not accept a stale enabled menu.
                    if(mask==0 || awakeEnabled) { engine.Select(mask); faulted=false; }
                    else hook.ResetSelection(engine.State.Selection);
                }
                switch(command)
                {
                    case 4: ShowStatus(); break;
                    case 5: Process.Start(new ProcessStartInfo("control.exe", "/name Microsoft.PowerOptions /page pageGlobalSettings") { UseShellExecute=true }); break;
                }
                if (hook.NativeError!=0) throw new IOException("菜单扩展失败，Win32 错误 "+hook.NativeError);
            }
            if (awake.HasValue && !faulted) engine.Tick(awake.Value && compatible);
            else if ((DateTime.UtcNow-unknownSince).TotalSeconds>3) engine.Recover();
            status=faulted ? "异常，联动已暂停；请查看状态" : hook==null ? "等待 PowerToys Awake" : !compatible ? "新版菜单待验证" : engine.Status;
            hook?.Update(engine.State.Selection,faulted?4:engine.State.Selection==0?0:!engine.IsActive?1:engine.Suppressed!=0?3:2,awakeEnabled);
        }
        catch(Exception error)
        {
            faulted=true;
            awakeEnabled=false;
            status="异常，联动已暂停";
            try { engine.Recover(); } catch(Exception recovery) { Log("恢复仍需处理："+recovery); }
            hook?.Update(engine.State.Selection,4);
            Warn(error.GetType().Name+error.Message,"合盖扩展异常："+error.Message+" 请查看状态及恢复记录。");
        }
        // Optional diagnostic I/O must not enter the power-control fault handler above.
        Program.Diagnostics.FlushLogs();
        Program.Diagnostics.PublishStatus(Snapshot());
    }
    string Snapshot() => JsonSerializer.Serialize(new {
        version=Program.Version, status, awakeVersion, targetPid=hook?.Pid, awakeEnabled,
        selectedAc=(engine.State.Selection&1)!=0, selectedDc=(engine.State.Selection&2)!=0,
        menuVerified=hook?.Menus>0, active=engine.IsActive, manualOverride=engine.Suppressed,
        pendingRestores=engine.State.Leases.Count, lastError, directory=Program.Home,
        diagnosticsWarning=Program.Diagnostics.Warning, pendingLogEntries=Program.Diagnostics.PendingLogEntries,
        droppedLogEntries=Program.Diagnostics.DroppedLogEntries,
        startup=File.Exists(Program.StartupLink)
    });
    async Task Listen()
    {
        while(!cancellation.IsCancellationRequested)
        {
            try
            {
                using var pipe=new NamedPipeServerStream(Program.PipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellation.Token);
                using var reader=new StreamReader(pipe,Encoding.UTF8,leaveOpen:true);
                using var writer=new StreamWriter(pipe,new UTF8Encoding(false),leaveOpen:true) { AutoFlush=true };
                var command=await reader.ReadLineAsync(cancellation.Token);
                var completion=new TaskCompletionSource<string>();
                dispatcher.BeginInvoke(() => {
                    try
                    {
                        if(command=="stop") { Shutdown(); completion.SetResult("OK: 已恢复设置并移除 Hook。"); }
                        else if(command=="show") { ShowStatus(); completion.SetResult("OK"); }
                        else completion.SetResult(Snapshot());
                    }
                    catch(Exception error) { completion.SetResult("ERROR: "+error.Message); }
                });
                await writer.WriteLineAsync(await completion.Task);
                if(closing) { dispatcher.BeginInvoke(ExitThread); return; }
            }
            catch(OperationCanceledException) { return; }
            catch(Exception error) { Log("控制接口："+error.Message); await Task.Delay(500); }
        }
    }
    void ShowStatus()
    {
        var form=new Form { Text="Awake 合盖扩展 "+Program.Version, Width=650,Height=430,StartPosition=FormStartPosition.CenterScreen };
        var text=new TextBox { Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,
            Text=$"状态：{status}\r\nAwake：{awakeVersion}\r\n插电联动：{(engine.State.Selection&1)!=0}\r\n电池联动：{(engine.State.Selection&2)!=0}\r\n待恢复记录：{engine.State.Leases.Count}\r\n最近异常：{lastError}\r\n诊断记录：{(Program.Diagnostics.Warning.Length==0?"正常":Program.Diagnostics.Warning)}\r\n\r\n目录：{Program.Home}\r\n\r\n修改仅在 Awake 维持唤醒期间生效。系统中手动改动的设置优先。" };
        var button=new Button { Text="跟随系统设置并停止外挂",Dock=DockStyle.Bottom,Height=42 };
        button.Click+=(_,_)=> { try { Shutdown(); form.Close(); ExitThread(); } catch(Exception e) { MessageBox.Show(e.Message,"恢复未完成"); } };
        form.Controls.Add(text); form.Controls.Add(button); form.Show();
    }
    void SessionEnding(object? sender,SessionEndingEventArgs args)
    {
        try { Shutdown(); } catch(Exception error) { Log("注销前恢复未完成："+error); }
    }
    void Shutdown()
    {
        engine.Stop(); // A failure here keeps both the guardian and the durable journal available.
        hook?.Dispose(); hook=null;
        closing=true; timer.Stop(); notification.Visible=false;
        SystemEvents.SessionEnding-=SessionEnding;
        Log("后台助手已停止，合盖设置已检查恢复，Hook 已移除。");
    }
    protected override void ExitThreadCore()
    {
        if(!closing) { try { Shutdown(); } catch(Exception error) { ExitCode=1; Log("退出时恢复失败："+error); } }
        cancellation.Cancel(); timer.Dispose(); notification.Dispose();
        base.ExitThreadCore();
    }
}
