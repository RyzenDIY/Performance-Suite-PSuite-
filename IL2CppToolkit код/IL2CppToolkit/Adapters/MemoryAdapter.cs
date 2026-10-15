// language: C#, file: Adapters/MemoryAdapter.cs
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

public static class MemoryAdapter
{
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr read);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr written);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr h);

    const uint PROCESS_VM_READ = 0x0010;
    const uint PROCESS_VM_WRITE = 0x0020;
    const uint PROCESS_VM_OPERATION = 0x0008;
    const uint PROCESS_QUERY_INFORMATION = 0x0400;

    public static IntPtr Handle { get; private set; }
    public static int Pid { get; private set; }
    public static IntPtr ModuleBase { get; private set; }
    public static int ModuleSize { get; private set; }
    public static string AttachedProcess { get; private set; }

    public static bool IsAttached => Handle != IntPtr.Zero && Handle != (IntPtr)(-1);

    public static bool Attach(string processName, string moduleName)
    {
        Detach();
        int pid = ProcessFinder.FindPid(processName);
        if (pid == 0) return false;

        IntPtr h = OpenProcess(PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION | PROCESS_QUERY_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return false;

        var (b, sz) = ProcessFinder.FindModule(pid, moduleName);
        if (b == IntPtr.Zero) { CloseHandle(h); return false; }

        Handle = h;
        Pid = pid;
        ModuleBase = b;
        ModuleSize = sz;
        AttachedProcess = processName;
        return true;
    }

    public static void Detach()
    {
        if (Handle != IntPtr.Zero && Handle != (IntPtr)(-1)) CloseHandle(Handle);
        Handle = IntPtr.Zero;
        Pid = 0;
        ModuleBase = IntPtr.Zero;
        ModuleSize = 0;
        AttachedProcess = null;
    }

    public static byte[] Read(IntPtr addr, int size)
    {
        if (!IsAttached || addr == IntPtr.Zero) return null;
        byte[] buf = new byte[size];
        return ReadProcessMemory(Handle, addr, buf, size, out _) ? buf : null;
    }

    public static T ReadStruct<T>(IntPtr addr) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        byte[] buf = new byte[size];
        if (!ReadProcessMemory(Handle, addr, buf, size, out _)) return default;
        var h = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try { return Marshal.PtrToStructure<T>(h.AddrOfPinnedObject()); }
        finally { h.Free(); }
    }

    public static IntPtr ReadPtr(IntPtr addr) => ReadStruct<IntPtr>(addr);
    public static int ReadInt(IntPtr addr) => ReadStruct<int>(addr);
    public static uint ReadUInt(IntPtr addr) => ReadStruct<uint>(addr);
    public static long ReadLong(IntPtr addr) => ReadStruct<long>(addr);
    public static float ReadFloat(IntPtr addr) => ReadStruct<float>(addr);
    public static ulong ReadULong(IntPtr addr) => ReadStruct<ulong>(addr);

    public static bool Write(IntPtr addr, byte[] data)
        => IsAttached && WriteProcessMemory(Handle, addr, data, data.Length, out _);

    public static bool IsReadable(IntPtr addr)
    {
        if (!IsAttached || addr == IntPtr.Zero) return false;
        byte[] b = new byte[1];
        return ReadProcessMemory(Handle, addr, b, 1, out _);
    }

    // IL2CPP System.String: [0x10] int length, [0x14] UTF-16 chars
    public static string ReadManagedString(IntPtr addr, int max = 64)
    {
        if (addr == IntPtr.Zero) return "";
        byte[] head = Read(addr, 0x14);
        if (head == null) return "";
        int len = BitConverter.ToInt32(head, 0x10);
        if (len <= 0 || len > max) return "";
        byte[] chars = Read(addr + 0x14, len * 2);
        if (chars == null) return "";
        try { return Encoding.Unicode.GetString(chars); } catch { return ""; }
    }

    // native char*
    public static string ReadCString(IntPtr addr, int max = 128)
    {
        if (addr == IntPtr.Zero) return "";
        byte[] buf = Read(addr, max);
        if (buf == null) return "";
        int len = Array.IndexOf(buf, (byte)0);
        if (len < 0) len = max;
        try { return Encoding.UTF8.GetString(buf, 0, len); } catch { return ""; }
    }
}