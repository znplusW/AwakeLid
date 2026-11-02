using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AwakeLid;
public sealed class WindowsPowerStore : IPowerStore
{
    static readonly Guid Buttons = new("4f971e89-eebd-4455-a8de-9e59040e7347");
    static readonly Guid Lid = new("5ca83367-6e45-459f-a27b-476b1d01c936");
    public Guid ActivePlan
    {
        get
        {
            Check(PowerGetActiveScheme(IntPtr.Zero, out var pointer));
            try { return Marshal.PtrToStructure<Guid>(pointer); }
            finally { LocalFree(pointer); }
        }
    }
    public uint Read(Guid plan, Supply supply)
    {
        uint value;
        Check(supply == Supply.Ac ? PowerReadACValueIndex(IntPtr.Zero, plan, Buttons, Lid, out value)
                                  : PowerReadDCValueIndex(IntPtr.Zero, plan, Buttons, Lid, out value));
        if (value > 3) throw new InvalidDataException("Windows 返回了无法识别的合盖设置。");
        return value;
    }
    public void Write(Guid plan, Supply supply, uint value)
    {
        if (value > 3) throw new ArgumentOutOfRangeException(nameof(value));
        Check(supply == Supply.Ac ? PowerWriteACValueIndex(IntPtr.Zero, plan, Buttons, Lid, value)
                                  : PowerWriteDCValueIndex(IntPtr.Zero, plan, Buttons, Lid, value));
        // Do not activate an old plan when cleaning up a previous lease.
        if (ActivePlan == plan) Check(PowerSetActiveScheme(IntPtr.Zero, plan));
    }
    static void Check(uint error) { if (error != 0) throw new Win32Exception((int)error); }
    [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr plan);
    [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(IntPtr root, in Guid plan, in Guid subgroup, in Guid setting, out uint value);
    [DllImport("powrprof.dll")] static extern uint PowerReadDCValueIndex(IntPtr root, in Guid plan, in Guid subgroup, in Guid setting, out uint value);
    [DllImport("powrprof.dll")] static extern uint PowerWriteACValueIndex(IntPtr root, in Guid plan, in Guid subgroup, in Guid setting, uint value);
    [DllImport("powrprof.dll")] static extern uint PowerWriteDCValueIndex(IntPtr root, in Guid plan, in Guid subgroup, in Guid setting, uint value);
    [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, in Guid plan);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
}
