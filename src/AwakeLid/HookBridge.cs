using System.ComponentModel;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace AwakeLid;
public sealed class HookSession : IDisposable
{
    public const uint Magic = 0x414C4944;
    public const int Selection = 16, Status = 20, Command = 24, Sequence = 28,
        GuardianTick = 32, HookTick = 36, Attached = 40, MenuSeen = 44, Error = 48, Enabled = 52, Detach = 56;
    readonly MemoryMappedFile memory;
    readonly MemoryMappedViewAccessor view;
    readonly IntPtr library, hook;
    readonly uint ping;
    uint lastCommand;
    bool disposed;
    public IntPtr Window { get; }
    public int Pid { get; }
    public string Version { get; }
    public uint Menus => view.ReadUInt32(MenuSeen);
    public uint NativeError => view.ReadUInt32(Error);
    public bool Connected => view.ReadUInt32(Attached) == 1 && unchecked((uint)Environment.TickCount - view.ReadUInt32(HookTick)) < 6000;

    public HookSession(IntPtr window, string dll, bool allowTestHost = false)
    {
        Window = window;
        uint thread = Native.GetWindowThreadProcessId(window, out uint pid);
        Pid = checked((int)pid);
        using var process = Process.GetProcessById(Pid);
        if (!allowTestHost && process.ProcessName != "PowerToys.Awake") throw new InvalidOperationException("目标不是 PowerToys Awake。");
        var executable = process.MainModule?.FileName ?? throw new InvalidOperationException("无法识别 Awake 路径。");
        Version = FileVersionInfo.GetVersionInfo(executable).FileVersion ?? "未知";
        memory = MemoryMappedFile.CreateNew($"Local\\AwakeLid.v1.{pid}", 64, MemoryMappedFileAccess.ReadWrite);
        view = memory.CreateViewAccessor(0, 64);
        try
        {
            view.Write(0, Magic); view.Write(4, 2u); view.Write(8, (uint)Environment.ProcessId); view.Write(12, pid);
            view.Write(GuardianTick, unchecked((uint)Environment.TickCount)); view.Write(Enabled, 1u);
            library = Native.LoadLibraryW(Path.GetFullPath(dll));
            if (library == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法加载 Hook DLL。");
            var address = Native.GetProcAddress(library, "AwakeLidHook");
            if (address == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            hook = Native.SetWindowsHookExW(4, address, library, thread);
            if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法安装 Awake 线程 Hook；请检查双方权限级别。");
            ping = Native.RegisterWindowMessageW("AwakeLid.Ping.v1");
            if (Native.SendMessageTimeoutW(window, ping, IntPtr.Zero, IntPtr.Zero, 2, 2000, out var reply) == IntPtr.Zero || reply.ToUInt64() != Magic || !Connected)
                throw new InvalidOperationException("Awake 未确认 Hook 已加载。可能是窗口结构、权限或兼容性变化。");
        }
        catch { Dispose(); throw; }
    }
    public void ResetSelection(int mask) => view.Write(60,mask);
    public void Update(int mask, int status, bool awakeEnabled=false)
    {
        if (lastCommand==0 && view.ReadUInt32(Sequence)==0) view.Write(60,mask);
        view.Write(Selection, mask); view.Write(Status, status|(awakeEnabled?0x100:0));
        view.Write(GuardianTick, unchecked((uint)Environment.TickCount));
    }
    public int ReadCommand()
    {
        uint sequence = view.ReadUInt32(Sequence);
        if (sequence == lastCommand) return 0;
        int command = view.ReadInt32(Command); lastCommand = sequence;
        return command;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            view.Write(Detach, 1); view.Write(Enabled, 0);
            if (Window != IntPtr.Zero && ping != 0)
                Native.SendMessageTimeoutW(Window, ping, IntPtr.Zero, IntPtr.Zero, 2, 1500, out _);
            if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            if (Window != IntPtr.Zero) Native.SendMessageTimeoutW(Window,0,IntPtr.Zero,IntPtr.Zero,2,1000,out _);
            if (library != IntPtr.Zero) Native.FreeLibrary(library);
        }
        finally { view.Dispose(); memory.Dispose(); }
    }
}

public static class Native
{
    [StructLayout(LayoutKind.Sequential)] struct ProcessBasicInfo
    {
        public IntPtr Reserved, Peb, Reserved2, Reserved3, Pid, Parent;
    }
    public static bool IsPowerToysChild(int pid)
    {
        try
        {
            using var target=Process.GetProcessById(pid);
            if(NtQueryInformationProcess(target.Handle,0,out var info,Marshal.SizeOf<ProcessBasicInfo>(),out _)!=0) return false;
            using var parent=Process.GetProcessById(info.Parent.ToInt32());
            return parent.ProcessName=="PowerToys";
        }
        catch(Exception error) when(error is ArgumentException or InvalidOperationException or Win32Exception) { return false; }
    }
    [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr process,int type,out ProcessBasicInfo info,int length,out int returned);
    public delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
    public static IntPtr FindAwake()
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            var name = new StringBuilder(128);
            GetClassNameW(window, name, name.Capacity);
            if (name.ToString() != "Awake.MessageWindow") return true;
            GetWindowThreadProcessId(window, out uint pid);
            try
            {
                using var process = Process.GetProcessById((int)pid);
                if (process.ProcessName == "PowerToys.Awake") { result = window; return false; }
            }
            catch (ArgumentException) { }
            return true;
        }, IntPtr.Zero);
        return result;
    }
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr window, StringBuilder name, int maxCount);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookExW(int id, IntPtr procedure, IntPtr module, uint thread);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessageW(string message);
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SendMessageTimeoutW(IntPtr window, uint message, IntPtr wp, IntPtr lp, uint flags, uint timeout, out UIntPtr result);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr LoadLibraryW(string file);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)] public static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll")] public static extern bool FreeLibrary(IntPtr module);
}
