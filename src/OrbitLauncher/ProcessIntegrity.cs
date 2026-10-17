using System;
using System.Runtime.InteropServices;

namespace OrbitLauncher;
internal static class ProcessIntegrity
{
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetTokenInformation(IntPtr token,int type,IntPtr data,int size,out int needed);
    [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthority(IntPtr sid,uint index);
    internal static readonly int Level=Read();
    internal static string Label=>Level switch {<0=>"未知",<8192=>"低完整性（受限）",<12288=>"普通用户",_=>"管理员"};
    static int Read()
    {
        if(!OpenProcessToken(GetCurrentProcess(),8,out var token))return -1;
        IntPtr buffer=IntPtr.Zero;
        try
        {
            GetTokenInformation(token,25,IntPtr.Zero,0,out int size);
            if(size<=0)return -1;
            buffer=Marshal.AllocHGlobal(size);
            if(!GetTokenInformation(token,25,buffer,size,out _))return -1;
            var sid=Marshal.ReadIntPtr(buffer);byte count=Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            return count==0?-1:Marshal.ReadInt32(GetSidSubAuthority(sid,(uint)(count-1)));
        }
        catch { return -1; }
        finally {if(buffer!=IntPtr.Zero)Marshal.FreeHGlobal(buffer);CloseHandle(token);}
    }
}
