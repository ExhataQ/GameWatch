using System.Net;
using System.Runtime.InteropServices;

namespace GameWatch.Services;

public enum TransportProtocol { Tcp, Udp }

public enum IpFamily { IPv4, IPv6 }

// One live connection or bound endpoint, associated with a PID. Kept as a
// plain immutable record with no OS types leaking out, so downstream code
// (ConnectionAggregator, the UI) never has to deal with family-specific
// shapes directly.
public record ConnectionRecord(
    int Pid,
    TransportProtocol Protocol,
    IpFamily Family,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    bool IsEstablished);

// Reads the live TCP/UDP connection tables directly from the OS via
// GetExtendedTcpTable / GetExtendedUdpTable (iphlpapi.dll) instead of
// spawning netstat.exe every couple of seconds. This removes a repeated
// external-process launch, and - unlike the old netstat.exe text
// parsing - gives correct IPv4 *and* IPv6 rows, plus UDP endpoints,
// straight from the OS-maintained tables. No extra privileges are needed
// beyond what the app already requires for ETW/Game Mode.
public static class NativeConnectionReader
{
    private const int AF_INET = 2;
    private const int AF_INET6 = 23;
    private const int MIB_TCP_STATE_ESTAB = 5;

    private enum TCP_TABLE_CLASS
    {
        TCP_TABLE_OWNER_PID_ALL = 5
    }

    private enum UDP_TABLE_CLASS
    {
        UDP_TABLE_OWNER_PID = 1
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ulAf, TCP_TABLE_CLASS tableClass, int reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(IntPtr pUdpTable, ref int dwOutBufLen, bool sort, int ulAf, UDP_TABLE_CLASS tableClass, int reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint state;
        public uint localAddr;
        public uint localPort;
        public uint remoteAddr;
        public uint remotePort;
        public uint owningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] localAddr;
        public uint localScopeId;
        public uint localPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] remoteAddr;
        public uint remoteScopeId;
        public uint remotePort;
        public uint state;
        public uint owningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDPROW_OWNER_PID
    {
        public uint localAddr;
        public uint localPort;
        public uint owningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] localAddr;
        public uint localScopeId;
        public uint localPort;
        public uint owningPid;
    }

    // dwLocalPort/dwRemotePort only use their low 16 bits, holding the
    // port in network byte order - this swaps those two bytes back to a
    // normal host-order ushort. Standard idiom for these structures.
    private static int ToPort(uint rawPort)
    {
        var bytes = BitConverter.GetBytes(rawPort);
        return (bytes[0] << 8) | bytes[1];
    }

    public static List<ConnectionRecord> GetAllConnections()
    {
        var list = new List<ConnectionRecord>();
        try { list.AddRange(ReadTcp(AF_INET)); } catch { }
        try { list.AddRange(ReadTcp(AF_INET6)); } catch { }
        try { list.AddRange(ReadUdp(AF_INET)); } catch { }
        try { list.AddRange(ReadUdp(AF_INET6)); } catch { }
        return list;
    }

    private static List<ConnectionRecord> ReadTcp(int af)
    {
        var results = new List<ConnectionRecord>();
        int bufSize = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref bufSize, false, af, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
        if (bufSize <= 0) return results;

        IntPtr buffer = Marshal.AllocHGlobal(bufSize);
        try
        {
            uint ret = GetExtendedTcpTable(buffer, ref bufSize, false, af, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
            if (ret != 0) return results;

            int numEntries = Marshal.ReadInt32(buffer);
            IntPtr rowPtr = IntPtr.Add(buffer, 4);

            if (af == AF_INET)
            {
                int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
                for (int i = 0; i < numEntries; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(IntPtr.Add(rowPtr, i * rowSize));
                    results.Add(new ConnectionRecord(
                        Pid: (int)row.owningPid,
                        Protocol: TransportProtocol.Tcp,
                        Family: IpFamily.IPv4,
                        LocalAddress: new IPAddress(row.localAddr).ToString(),
                        LocalPort: ToPort(row.localPort),
                        RemoteAddress: new IPAddress(row.remoteAddr).ToString(),
                        RemotePort: ToPort(row.remotePort),
                        IsEstablished: row.state == MIB_TCP_STATE_ESTAB));
                }
            }
            else
            {
                int rowSize = Marshal.SizeOf<MIB_TCP6ROW_OWNER_PID>();
                for (int i = 0; i < numEntries; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_TCP6ROW_OWNER_PID>(IntPtr.Add(rowPtr, i * rowSize));
                    results.Add(new ConnectionRecord(
                        Pid: (int)row.owningPid,
                        Protocol: TransportProtocol.Tcp,
                        Family: IpFamily.IPv6,
                        LocalAddress: new IPAddress(row.localAddr).ToString(),
                        LocalPort: ToPort(row.localPort),
                        RemoteAddress: new IPAddress(row.remoteAddr).ToString(),
                        RemotePort: ToPort(row.remotePort),
                        IsEstablished: row.state == MIB_TCP_STATE_ESTAB));
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return results;
    }

    private static List<ConnectionRecord> ReadUdp(int af)
    {
        var results = new List<ConnectionRecord>();
        int bufSize = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref bufSize, false, af, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);
        if (bufSize <= 0) return results;

        IntPtr buffer = Marshal.AllocHGlobal(bufSize);
        try
        {
            uint ret = GetExtendedUdpTable(buffer, ref bufSize, false, af, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);
            if (ret != 0) return results;

            int numEntries = Marshal.ReadInt32(buffer);
            IntPtr rowPtr = IntPtr.Add(buffer, 4);

            // UDP is connectionless - there's no remote endpoint or state
            // in these rows, just "this PID has a socket bound to this
            // local port". We surface that as an always-active endpoint.
            if (af == AF_INET)
            {
                int rowSize = Marshal.SizeOf<MIB_UDPROW_OWNER_PID>();
                for (int i = 0; i < numEntries; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(IntPtr.Add(rowPtr, i * rowSize));
                    results.Add(new ConnectionRecord(
                        Pid: (int)row.owningPid,
                        Protocol: TransportProtocol.Udp,
                        Family: IpFamily.IPv4,
                        LocalAddress: new IPAddress(row.localAddr).ToString(),
                        LocalPort: ToPort(row.localPort),
                        RemoteAddress: "",
                        RemotePort: 0,
                        IsEstablished: true));
                }
            }
            else
            {
                int rowSize = Marshal.SizeOf<MIB_UDP6ROW_OWNER_PID>();
                for (int i = 0; i < numEntries; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_UDP6ROW_OWNER_PID>(IntPtr.Add(rowPtr, i * rowSize));
                    results.Add(new ConnectionRecord(
                        Pid: (int)row.owningPid,
                        Protocol: TransportProtocol.Udp,
                        Family: IpFamily.IPv6,
                        LocalAddress: new IPAddress(row.localAddr).ToString(),
                        LocalPort: ToPort(row.localPort),
                        RemoteAddress: "",
                        RemotePort: 0,
                        IsEstablished: true));
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return results;
    }
}
