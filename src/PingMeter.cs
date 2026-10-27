// Reads the round-trip time of the TCP connection the game client (SFrame.exe)
// already has open to its server. Nothing is sent to the server: Windows keeps
// RTT statistics for every TCP connection (TCP ESTATS), we only switch them on
// for the game's connections and read them. Switching them on needs admin.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace RappelzPing
{
    enum MeterState { NoGame, NotConnected, Measuring, Ping, NoAdmin }

    class PingMeter
    {
        public static string ProcessName = "SFrame"; // --process=chrome for testing
        const int WindowSeconds = 3;   // shown value: average of the last 3 seconds of samples
        const int TcpEstablished = 5;  // MIB_TCP_STATE_ESTAB
        const int AccessDenied = 5;    // ERROR_ACCESS_DENIED

        public MeterState State { get; private set; }
        public int Ms { get; private set; }
        public string Server { get; private set; }
        /// <summary>True when this tick produced a value from new samples.</summary>
        public bool Fresh { get; private set; }

        int pid;
        Conn selected;
        readonly Dictionary<string, Conn> conns = new Dictionary<string, Conn>();

        class Conn
        {
            public TcpRow Row;
            public string Key;
            public bool Enabled, Seen, First = true;
            public uint LastSum, LastCount;
            public readonly Queue<long[]> Window = new Queue<long[]>(); // (sum ms, sample count) per tick
            public long WindowCount;
        }

        public void Tick()
        {
            Fresh = false;
            int p = FindGamePid();
            if (p == 0)
            {
                State = MeterState.NoGame; Server = null; selected = null; conns.Clear();
                return;
            }
            if (p != pid) { pid = p; selected = null; conns.Clear(); }

            foreach (var c in conns.Values) c.Seen = false;
            foreach (var row in Native.TcpTable(pid))
            {
                if (row.State != TcpEstablished || row.RemoteAddrRaw == 0) continue;
                if (IPAddress.IsLoopback(new IPAddress(row.RemoteAddrRaw))) continue;
                string k = row.LocalAddr + ":" + row.LocalPort + ">" + row.RemoteAddr + ":" + row.RemotePort;
                Conn c;
                if (!conns.TryGetValue(k, out c)) { c = new Conn { Row = row, Key = k }; conns[k] = c; }
                c.Seen = true;
            }
            var gone = new List<string>();
            foreach (var kv in conns) if (!kv.Value.Seen) gone.Add(kv.Key);
            foreach (var k in gone) { if (selected != null && selected.Key == k) selected = null; conns.Remove(k); }

            if (conns.Count == 0) { State = MeterState.NotConnected; Server = null; return; }

            bool denied = false;
            foreach (var c in conns.Values)
            {
                if (!c.Enabled)
                {
                    int e = Native.EnableStats(ref c.Row);
                    if (e == AccessDenied) { denied = true; continue; }
                    if (e != 0) continue;
                    c.Enabled = true;
                }
                uint sum, count;
                if (!Native.ReadRtt(ref c.Row, out sum, out count)) continue;
                if (c.First) { c.First = false; c.LastSum = sum; c.LastCount = count; continue; }
                long dSum = unchecked(sum - c.LastSum), dCount = unchecked(count - c.LastCount);
                c.LastSum = sum; c.LastCount = count;
                c.Window.Enqueue(new[] { dSum, dCount });
                c.WindowCount += dCount;
                while (c.Window.Count > WindowSeconds) c.WindowCount -= c.Window.Dequeue()[1];
            }
            if (denied) { State = MeterState.NoAdmin; return; }

            // With several connections, follow the busiest one (most RTT samples).
            Conn best = null;
            foreach (var c in conns.Values)
                if (c.WindowCount > 0 && (best == null || c.WindowCount > best.WindowCount)) best = c;
            if (best != null) selected = best;

            if (selected == null) { State = MeterState.Measuring; Server = null; return; }

            Server = selected.Row.RemoteAddr + ":" + selected.Row.RemotePort;
            long totalSum = 0, totalCount = 0;
            foreach (var w in selected.Window) { totalSum += w[0]; totalCount += w[1]; }
            // If the game was quiet for a few seconds, keep showing the last value.
            if (totalCount > 0) { Ms = (int)Math.Round((double)totalSum / totalCount); Fresh = true; }
            State = MeterState.Ping;
        }

        // Several clients open: the one in front, else the previous one, else any.
        int FindGamePid()
        {
            var procs = Process.GetProcessesByName(ProcessName);
            if (procs.Length == 0) return 0;
            var ids = new HashSet<int>();
            foreach (var p in procs) { ids.Add(p.Id); p.Dispose(); }
            uint front;
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out front);
            if (ids.Contains((int)front)) return (int)front;
            if (ids.Contains(pid)) return pid;
            foreach (int id in ids) return id;
            return 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TcpRow // MIB_TCPROW
    {
        public uint State, LocalAddrRaw, LocalPortRaw, RemoteAddrRaw, RemotePortRaw;
        public string LocalAddr { get { return new IPAddress(LocalAddrRaw).ToString(); } }
        public string RemoteAddr { get { return new IPAddress(RemoteAddrRaw).ToString(); } }
        public int LocalPort { get { return Swap(LocalPortRaw); } }
        public int RemotePort { get { return Swap(RemotePortRaw); } }
        static int Swap(uint p) { return (int)(((p & 0xff) << 8) | ((p >> 8) & 0xff)); }
    }

    static class Native
    {
        const int AF_INET = 2, TCP_TABLE_OWNER_PID_ALL = 5, TcpConnectionEstatsPath = 3;
        // TCP_ESTATS_PATH_ROD_v0 is 40 ULONGs; SumRtt and CountRtt (milliseconds).
        const int RodLength = 40, SumRtt = 31, CountRtt = 32;

        [DllImport("iphlpapi.dll")]
        static extern int GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int cls, int reserved);
        [DllImport("iphlpapi.dll")]
        static extern int SetPerTcpConnectionEStats(ref TcpRow row, int type, byte[] rw, int ver, int size, int offset);
        [DllImport("iphlpapi.dll")]
        static extern int GetPerTcpConnectionEStats(ref TcpRow row, int type, IntPtr rw, int rwVer, int rwSize,
            IntPtr ros, int rosVer, int rosSize, uint[] rod, int rodVer, int rodSize);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        public static List<TcpRow> TcpTable(int pid)
        {
            var list = new List<TcpRow>();
            int size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                size += 4096; // the table may grow between the two calls
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    if (GetExtendedTcpTable(buf, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) != 0) continue;
                    int n = Marshal.ReadInt32(buf);
                    for (int i = 0; i < n; i++)
                    {
                        IntPtr p = buf + 4 + i * 24; // MIB_TCPROW_OWNER_PID
                        if (Marshal.ReadInt32(p + 20) != pid) continue;
                        list.Add(new TcpRow
                        {
                            State = (uint)Marshal.ReadInt32(p),
                            LocalAddrRaw = (uint)Marshal.ReadInt32(p + 4),
                            LocalPortRaw = (uint)Marshal.ReadInt32(p + 8),
                            RemoteAddrRaw = (uint)Marshal.ReadInt32(p + 12),
                            RemotePortRaw = (uint)Marshal.ReadInt32(p + 16),
                        });
                    }
                    return list;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            return list;
        }

        public static int EnableStats(ref TcpRow row)
        {
            return SetPerTcpConnectionEStats(ref row, TcpConnectionEstatsPath, new byte[] { 1 }, 0, 1, 0);
        }

        public static bool ReadRtt(ref TcpRow row, out uint sum, out uint count)
        {
            var rod = new uint[RodLength];
            int e = GetPerTcpConnectionEStats(ref row, TcpConnectionEstatsPath, IntPtr.Zero, 0, 0,
                IntPtr.Zero, 0, 0, rod, 0, RodLength * 4);
            sum = rod[SumRtt]; count = rod[CountRtt];
            return e == 0;
        }
    }
}
