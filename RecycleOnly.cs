using System;
using System.IO;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Text;
using Microsoft.Win32.SafeHandles;

// Windows 8+ recycle-only operation. A progress sink vetoes non-recycle deletes.
public static class AwakeLidRecycleOnly
{
    [StructLayout(LayoutKind.Sequential)] struct BinInfo { public uint Size; public long Bytes, Items; }
    [StructLayout(LayoutKind.Sequential)] struct TagInfo { public uint Attributes, Tag; }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetFileInformationByHandleEx(SafeFileHandle file,int kind,out TagInfo info,uint size);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetFinalPathNameByHandle(SafeFileHandle file,StringBuilder path,uint length,uint flags);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern int SHQueryRecycleBin(string root,ref BinInfo info);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=false)] static extern void SHCreateItemFromParsingName(string path,IntPtr context,ref Guid iid,out IShellItem item);
    // CLOUD/CLOUD_1 ... CLOUD_F are placeholders, not name-surrogate redirects.
    public static bool IsSupportedReparseTag(uint tag)
    {
        return tag==0 || (tag&0xffff0fffu)==0x9000001au;
    }
    static SafeFileHandle OpenMetadata(string path,bool noFollow)
    {
        var handle=CreateFile(Path.GetFullPath(path),0x80,7,IntPtr.Zero,3,0x02100000u|(noFollow?0x00200000u:0),IntPtr.Zero);
        if(handle.IsInvalid) { int error=Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error,"无法核实回收对象。"); }
        return handle;
    }
    public static uint ValidateTarget(string path)
    {
        using(var handle=OpenMetadata(path,true))
        {
            TagInfo info;
            if(!GetFileInformationByHandleEx(handle,9,out info,8)) throw new Win32Exception(Marshal.GetLastWin32Error());
            uint tag=(info.Attributes&0x400)!=0?info.Tag:0;
            if(!IsSupportedReparseTag(tag))
                throw new IOException("回收目标本身是目录联接、符号链接或不支持的重解析点（0x"+tag.ToString("X8")+"），停止处理。");
            return tag;
        }
    }
    public static string CanonicalPath(string path)
    {
        using(var handle=OpenMetadata(path,false))
        {
            var output=new StringBuilder(32768);
            uint length=GetFinalPathNameByHandle(handle,output,(uint)output.Capacity,0);
            if(length==0||length>=output.Capacity) throw new Win32Exception(Marshal.GetLastWin32Error(),"无法核实路径指向。");
            string canonical=output.ToString();
            if(canonical.StartsWith(@"\\?\UNC\",StringComparison.OrdinalIgnoreCase)) return @"\\"+canonical.Substring(8);
            if(canonical.StartsWith(@"\\?\",StringComparison.OrdinalIgnoreCase)) return canonical.Substring(4).TrimEnd('\\');
            return canonical.TrimEnd('\\');
        }
    }
    public static string Send(string path)
    {
        path=Path.GetFullPath(path);
        if(!File.Exists(path)&&!Directory.Exists(path)) throw new IOException("回收对象不存在。");
        ValidateTarget(path);
        var volume=Path.GetPathRoot(path);
        if(new DriveInfo(volume).DriveType!=DriveType.Fixed) throw new IOException("仅对可核实回收站的本地固定磁盘执行卸载。");
        var info=new BinInfo {Size=(uint)Marshal.SizeOf(typeof(BinInfo))};
        Marshal.ThrowExceptionForHR(SHQueryRecycleBin(volume,ref info));
        var operation=(IFileOperation)new FileOperation();
        IShellItem item=null;
        try
        {
            // Explicit recycle, undo record, early failure, no UI, no connected-item deletion.
            Marshal.ThrowExceptionForHR(operation.SetOperationFlags(0x20000000|0x00080000|0x00100000|0x4000|0x0400|0x2000|0x0010|0x0004));
            var iid=typeof(IShellItem).GUID;
            SHCreateItemFromParsingName(path,IntPtr.Zero,ref iid,out item);
            var sink=new RecycleSink();
            Marshal.ThrowExceptionForHR(operation.DeleteItem(item,sink));
            Marshal.ThrowExceptionForHR(operation.PerformOperations());
            bool aborted; Marshal.ThrowExceptionForHR(operation.GetAnyOperationsAborted(out aborted));
            if(aborted||!sink.Confirmed) throw new IOException("未能确认对象进入回收站；操作已停止，不使用永久删除。");
            return sink.NewPath;
        }
        finally
        {
            if(item!=null) Marshal.FinalReleaseComObject(item);
            Marshal.FinalReleaseComObject(operation);
        }
    }
    [ComImport,Guid("3AD05575-8857-4850-9277-11B85BDB8E09")] class FileOperation { }
    [ComImport,Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr context,ref Guid handler,ref Guid iid,out IntPtr result);
        [PreserveSig] int GetParent(out IShellItem parent);
        [PreserveSig] int GetDisplayName(uint kind,out IntPtr text);
        [PreserveSig] int GetAttributes(uint mask,out uint attributes);
        [PreserveSig] int Compare(IShellItem other,uint hint,out int order);
    }
    [ComImport,Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileOperation
    {
        [PreserveSig] int Advise(IProgress sink,out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOperationFlags(uint flags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)]string message);
        [PreserveSig] int SetProgressDialog(IntPtr dialog);
        [PreserveSig] int SetProperties(IntPtr changes);
        [PreserveSig] int SetOwnerWindow(IntPtr window);
        [PreserveSig] int ApplyPropertiesToItem(IShellItem item);
        [PreserveSig] int ApplyPropertiesToItems(IntPtr items);
        [PreserveSig] int RenameItem(IShellItem item,[MarshalAs(UnmanagedType.LPWStr)]string name,IProgress sink);
        [PreserveSig] int RenameItems(IntPtr items,[MarshalAs(UnmanagedType.LPWStr)]string name);
        [PreserveSig] int MoveItem(IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string name,IProgress sink);
        [PreserveSig] int MoveItems(IntPtr items,IShellItem destination);
        [PreserveSig] int CopyItem(IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string name,IProgress sink);
        [PreserveSig] int CopyItems(IntPtr items,IShellItem destination);
        [PreserveSig] int DeleteItem(IShellItem item,IProgress sink);
        [PreserveSig] int DeleteItems(IntPtr items);
        [PreserveSig] int NewItem(IShellItem destination,uint attributes,[MarshalAs(UnmanagedType.LPWStr)]string name,[MarshalAs(UnmanagedType.LPWStr)]string template,IProgress sink);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)]out bool aborted);
    }
    [ComVisible(true),Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IProgress
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags,IShellItem item,[MarshalAs(UnmanagedType.LPWStr)]string name);
        [PreserveSig] int PostRenameItem(uint flags,IShellItem item,[MarshalAs(UnmanagedType.LPWStr)]string name,int result,IShellItem created);
        [PreserveSig] int PreMoveItem(uint flags,IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string name);
        [PreserveSig] int PostMoveItem(uint flags,IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string name,int result,IShellItem created);
        [PreserveSig] int PreCopyItem(uint flags,IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string name);
        [PreserveSig] int PostCopyItem(uint flags,IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string name,int result,IShellItem created);
        [PreserveSig] int PreDeleteItem(uint flags,IShellItem item);
        [PreserveSig] int PostDeleteItem(uint flags,IShellItem item,int result,IShellItem created);
        [PreserveSig] int PreNewItem(uint flags,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string name);
        [PreserveSig] int PostNewItem(uint flags,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)]string name,[MarshalAs(UnmanagedType.LPWStr)]string template,uint attributes,int result,IShellItem created);
        [PreserveSig] int UpdateProgress(uint total,uint done);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }
    [ComVisible(true),ClassInterface(ClassInterfaceType.None)] public sealed class RecycleSink : IProgress
    {
        public bool Confirmed; public string NewPath="";
        public int PreDeleteItem(uint flags,IShellItem item) { return (flags&0x80)!=0?0:unchecked((int)0x80004004); }
        public int PostDeleteItem(uint flags,IShellItem item,int result,IShellItem created)
        {
            if(result<0||created==null) return result<0?result:unchecked((int)0x80004005);
            IntPtr text;
            if(created.GetDisplayName(0x80058000,out text)<0) return unchecked((int)0x80004005);
            try { NewPath=Marshal.PtrToStringUni(text); Confirmed=NewPath!=null&&NewPath.IndexOf("\\$Recycle.Bin\\",StringComparison.OrdinalIgnoreCase)>=0; }
            finally { Marshal.FreeCoTaskMem(text); }
            return Confirmed?0:unchecked((int)0x80004005);
        }
        public int StartOperations(){return 0;} public int FinishOperations(int result){return 0;}
        public int PreRenameItem(uint f,IShellItem i,string n){return 0;} public int PostRenameItem(uint f,IShellItem i,string n,int h,IShellItem c){return 0;}
        public int PreMoveItem(uint f,IShellItem i,IShellItem d,string n){return 0;} public int PostMoveItem(uint f,IShellItem i,IShellItem d,string n,int h,IShellItem c){return 0;}
        public int PreCopyItem(uint f,IShellItem i,IShellItem d,string n){return 0;} public int PostCopyItem(uint f,IShellItem i,IShellItem d,string n,int h,IShellItem c){return 0;}
        public int PreNewItem(uint f,IShellItem d,string n){return 0;} public int PostNewItem(uint f,IShellItem d,string n,string t,uint a,int h,IShellItem c){return 0;}
        public int UpdateProgress(uint t,uint d){return 0;} public int ResetTimer(){return 0;} public int PauseTimer(){return 0;} public int ResumeTimer(){return 0;}
    }
}
