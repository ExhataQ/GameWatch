using System.Net;
using System.Runtime.InteropServices;

namespace GameWatch.Services;

public record ModuleConnection(ConnectionRecord Connection, string ModuleName);

public static class NativeOwnerModuleReader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Tcp4Row
    {
        public uint State, LocalAddress, LocalPort, RemoteAddress, RemotePort, Pid;
        public long Created;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public ulong[] ModuleInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Tcp6Row
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddress;
        public uint LocalScope, LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] RemoteAddress;
        public uint RemoteScope, RemotePort, State, Pid;
        public long Created;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public ulong[] ModuleInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Udp4Row
    {
        public uint LocalAddress, LocalPort, Pid;
        public long Created;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public ulong[] ModuleInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Udp6Row
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddress;
        public uint LocalScope, LocalPort, Pid;
        public long Created;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public ulong[] ModuleInfo;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Tcp4Table { public uint Count; public Tcp4Row First; }
    [StructLayout(LayoutKind.Sequential)] private struct Tcp6Table { public uint Count; public Tcp6Row First; }
    [StructLayout(LayoutKind.Sequential)] private struct Udp4Table { public uint Count; public Udp4Row First; }
    [StructLayout(LayoutKind.Sequential)] private struct Udp6Table { public uint Count; public Udp6Row First; }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, uint reserved);
    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, uint reserved);
    [DllImport("iphlpapi.dll")]
    private static extern uint GetOwnerModuleFromTcpEntry(IntPtr entry, int infoClass, IntPtr buffer, ref uint size);
    [DllImport("iphlpapi.dll")]
    private static extern uint GetOwnerModuleFromTcp6Entry(IntPtr entry, int infoClass, IntPtr buffer, ref uint size);
    [DllImport("iphlpapi.dll")]
    private static extern uint GetOwnerModuleFromUdpEntry(IntPtr entry, int infoClass, IntPtr buffer, ref uint size);
    [DllImport("iphlpapi.dll")]
    private static extern uint GetOwnerModuleFromUdp6Entry(IntPtr entry, int infoClass, IntPtr buffer, ref uint size);

    private delegate uint TableCall(IntPtr buffer, ref int size);
    private delegate uint ModuleCall(IntPtr entry, int infoClass, IntPtr buffer, ref uint size);

    private static int Port(uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        return (bytes[0] << 8) | bytes[1];
    }

    public static IReadOnlyList<ModuleConnection> ReadForPid(int pid)
    {
        var results = new List<ModuleConnection>();
        ReadTable<Tcp4Row>(pid, (IntPtr buffer, ref int size) => GetExtendedTcpTable(buffer, ref size, false, 2, 8, 0),
            GetOwnerModuleFromTcpEntry, (int)Marshal.OffsetOf<Tcp4Table>(nameof(Tcp4Table.First)),
            row => (int)row.Pid, row => new ConnectionRecord((int)row.Pid, TransportProtocol.Tcp, IpFamily.IPv4,
                new IPAddress(row.LocalAddress).ToString(), Port(row.LocalPort), new IPAddress(row.RemoteAddress).ToString(), Port(row.RemotePort), row.State == 5), results);
        ReadTable<Tcp6Row>(pid, (IntPtr buffer, ref int size) => GetExtendedTcpTable(buffer, ref size, false, 23, 8, 0),
            GetOwnerModuleFromTcp6Entry, (int)Marshal.OffsetOf<Tcp6Table>(nameof(Tcp6Table.First)),
            row => (int)row.Pid, row => new ConnectionRecord((int)row.Pid, TransportProtocol.Tcp, IpFamily.IPv6,
                new IPAddress(row.LocalAddress).ToString(), Port(row.LocalPort), new IPAddress(row.RemoteAddress).ToString(), Port(row.RemotePort), row.State == 5), results);
        ReadTable<Udp4Row>(pid, (IntPtr buffer, ref int size) => GetExtendedUdpTable(buffer, ref size, false, 2, 2, 0),
            GetOwnerModuleFromUdpEntry, (int)Marshal.OffsetOf<Udp4Table>(nameof(Udp4Table.First)),
            row => (int)row.Pid, row => new ConnectionRecord((int)row.Pid, TransportProtocol.Udp, IpFamily.IPv4,
                new IPAddress(row.LocalAddress).ToString(), Port(row.LocalPort), "", 0, true), results);
        ReadTable<Udp6Row>(pid, (IntPtr buffer, ref int size) => GetExtendedUdpTable(buffer, ref size, false, 23, 2, 0),
            GetOwnerModuleFromUdp6Entry, (int)Marshal.OffsetOf<Udp6Table>(nameof(Udp6Table.First)),
            row => (int)row.Pid, row => new ConnectionRecord((int)row.Pid, TransportProtocol.Udp, IpFamily.IPv6,
                new IPAddress(row.LocalAddress).ToString(), Port(row.LocalPort), "", 0, true), results);
        return results;
    }

    private static void ReadTable<TRow>(int pid, TableCall getTable, ModuleCall getModule, int offset,
        Func<TRow, int> owningPid, Func<TRow, ConnectionRecord> toConnection, List<ModuleConnection> results) where TRow : struct
    {
        var size = 0;
        getTable(IntPtr.Zero, ref size);
        if (size <= offset) return;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (getTable(buffer, ref size) != 0) return;
            var count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<TRow>();
            for (var index = 0; index < count; index++)
            {
                var entry = IntPtr.Add(buffer, offset + index * rowSize);
                var row = Marshal.PtrToStructure<TRow>(entry);
                if (owningPid(row) != pid) continue;
                var module = GetModuleName(entry, getModule);
                results.Add(new ModuleConnection(toConnection(row), module));
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string GetModuleName(IntPtr entry, ModuleCall getModule)
    {
        uint size = 0;
        if (getModule(entry, 0, IntPtr.Zero, ref size) != 122 || size < IntPtr.Size * 2) return "";
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (getModule(entry, 0, buffer, ref size) != 0) return "";
            return Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer)) ?? "";
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
