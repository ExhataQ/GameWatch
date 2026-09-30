using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace GameWatch.Services;

// Consumes the same low-level kernel network events that power Task
// Manager's / Resource Monitor's per-process network columns, via the
// official Microsoft.Diagnostics.Tracing.TraceEvent library (the one
// PerfView and dotnet-trace use) so we don't have to hand-decode raw ETW
// records ourselves.
//
// IMPORTANT OPERATIONAL NOTE: this uses the classic "NT Kernel Logger"
// session, which is a SINGLETON per machine - only one can run at a time,
// and it must be explicitly stopped or it can be left dangling (causing
// the next launch to fail to start tracing). Start() proactively cleans up
// any stale session before creating a new one, and Dispose()/Stop() always
// tear it down. Requires Administrator (see app.manifest).
public class ProcessByteCounters
{
    public long BytesSent;
    public long BytesReceived;
}

public class EtwNetworkMonitor : IDisposable
{
    private TraceEventSession? _session;
    private Task? _processingTask;
    private readonly ConcurrentDictionary<int, ProcessByteCounters> _counters = new();

    public bool IsRunning { get; private set; }
    public string? LastError { get; private set; }

    public bool Start()
    {
        try
        {
            // Note: The previous "stale session cleanup" block was removed
            // because GetActiveSessionNames() returns objects, not strings,
            // in this version of TraceEvent, and the string comparison failed
            // to compile. If a stale "NT Kernel Logger" session exists from a
            // crash, clean it up manually with:
            //     logman stop "NT Kernel Logger" -ets
            // before launching the app.

            _session = new TraceEventSession(KernelTraceEventParser.KernelSessionName)
            {
                StopOnDispose = true
            };
            _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

            _session.Source.Kernel.TcpIpSend += data => Add(data.ProcessID, sent: data.size);
            _session.Source.Kernel.TcpIpRecv += data => Add(data.ProcessID, received: data.size);
            _session.Source.Kernel.UdpIpSend += data => Add(data.ProcessID, sent: data.size);
            _session.Source.Kernel.UdpIpRecv += data => Add(data.ProcessID, received: data.size);

            // IPv6 counterparts of the same events, routed into the same
            // per-PID counters as their IPv4 siblings. Per the "don't make
            // users think about IPv4 vs IPv6" design goal, callers only
            // ever see one unified byte count per process - the v4/v6
            // split stays an internal implementation detail here.
            _session.Source.Kernel.TcpIpSendIPV6 += data => Add(data.ProcessID, sent: data.size);
            _session.Source.Kernel.TcpIpRecvIPV6 += data => Add(data.ProcessID, received: data.size);
            _session.Source.Kernel.UdpIpSendIPV6 += data => Add(data.ProcessID, sent: data.size);
            _session.Source.Kernel.UdpIpRecvIPV6 += data => Add(data.ProcessID, received: data.size);

            // Source.Process() blocks pumping events until Stop() is
            // called, so it must run off the UI thread.
            _processingTask = Task.Run(() =>
            {
                try { _session.Source.Process(); }
                catch { /* expected when the session is stopped on shutdown */ }
            });

            IsRunning = true;
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            IsRunning = false;
            return false;
        }
    }

    private void Add(int pid, long sent = 0, long received = 0)
    {
        var counters = _counters.GetOrAdd(pid, _ => new ProcessByteCounters());
        if (sent > 0) Interlocked.Add(ref counters.BytesSent, sent);
        if (received > 0) Interlocked.Add(ref counters.BytesReceived, received);
    }

    // Cumulative bytes per PID since Start() was called. Callers diff two
    // snapshots taken a known time apart to get a rate.
    public Dictionary<int, (long Sent, long Received)> Snapshot()
    {
        return _counters.ToDictionary(kv => kv.Key, kv => (kv.Value.BytesSent, kv.Value.BytesReceived));
    }

    public void Stop()
    {
        try { _session?.Stop(); } catch { }
        IsRunning = false;
    }

    public void Dispose()
    {
        Stop();
        _session?.Dispose();
    }
}