// Anonymous usage counter (can be turned off from the right-click menu).
// Sends: a random install ID (a new GUID, not derived from the hardware), a random
// session ID, the app version, how long this session has been running and a ping
// summary with the game server address. Nothing else. Network errors are ignored.
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace RappelzPing
{
    class Usage
    {
        const string Endpoint = "https://rappelzping.thebatur.com/api/event";
        const int HeartbeatMinutes = 10;

        readonly string install, session = Guid.NewGuid().ToString();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly object gate = new object();
        int min = int.MaxValue, max, samples;
        long sum;
        string server;
        TimeSpan nextBeat = TimeSpan.Zero;

        public Usage(string installId)
        {
            install = installId;
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
        }

        public void AddSample(int ms, string gameServer)
        {
            lock (gate)
            {
                if (ms < min) min = ms;
                if (ms > max) max = ms;
                sum += ms; samples++;
                server = gameServer;
            }
        }

        /// <summary>Called every second; sends on start and every few minutes.</summary>
        public void Tick(bool enabled)
        {
            if (!enabled || clock.Elapsed < nextBeat) return;
            nextBeat = clock.Elapsed + TimeSpan.FromMinutes(HeartbeatMinutes);
            string body = Json();
            ThreadPool.QueueUserWorkItem(_ => Post(body, 5000));
        }

        /// <summary>Last update when the app closes; short timeout so closing stays snappy.</summary>
        public void Final(bool enabled)
        {
            if (enabled && clock.Elapsed > TimeSpan.FromSeconds(30)) Post(Json(), 2000);
        }

        string Json()
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("{\"install\":\"").Append(install)
              .Append("\",\"session\":\"").Append(session)
              .Append("\",\"version\":\"").Append(Program.Version)
              .Append("\",\"seconds\":").Append(((long)clock.Elapsed.TotalSeconds).ToString(ci));
            lock (gate)
            {
                if (samples > 0)
                    sb.Append(",\"ping\":{\"min\":").Append(min.ToString(ci))
                      .Append(",\"avg\":").Append(((int)Math.Round((double)sum / samples)).ToString(ci))
                      .Append(",\"max\":").Append(max.ToString(ci))
                      .Append(",\"samples\":").Append(samples.ToString(ci)).Append('}');
                if (server != null) sb.Append(",\"server\":\"").Append(server).Append('"');
            }
            return sb.Append('}').ToString();
        }

        static void Post(string body, int timeoutMs)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(Endpoint);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.UserAgent = "RappelzPingTracker/" + Program.Version;
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                byte[] data = Encoding.UTF8.GetBytes(body);
                req.ContentLength = data.Length;
                using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);
                using (req.GetResponse()) { }
            }
            catch { } // offline, blocked, server down: never bother the user
        }
    }
}
