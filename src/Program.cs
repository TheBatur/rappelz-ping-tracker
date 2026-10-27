using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Rappelz Ping Tracker")]
[assembly: AssemblyProduct("Rappelz Ping Tracker")]
[assembly: AssemblyDescription("Live server ping overlay for Rappelz")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace RappelzPing
{
    static class Program
    {
        public const string Version = "1.1.0";
        public const string RepoUrl = "https://github.com/TheBatur/rappelz-ping-tracker";

        [STAThread]
        static void Main(string[] args)
        {
            bool first;
            using (new Mutex(true, "RappelzPing_SingleInstance", out first))
            {
                if (!first) return; // already running
                foreach (var a in args)
                {
                    if (a.StartsWith("--process=")) PingMeter.ProcessName = a.Substring(10);
                    else if (a == "--lang=en") T.Turkish = false;
                    else if (a == "--lang=tr") T.Turkish = true;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                if (!Settings.UsageAsked)
                {
                    using (var f = new ConsentForm()) Settings.UsageEnabled = f.ShowDialog() == DialogResult.OK;
                    Settings.UsageAsked = true;
                }
                Application.Run(new Overlay());
            }
        }
    }

    /// <summary>Small settings in HKCU\Software\RappelzPing.</summary>
    static class Settings
    {
        const string KeyPath = @"Software\RappelzPing";

        static object Get(string name)
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(KeyPath)) return k == null ? null : k.GetValue(name); }
            catch { return null; }
        }

        static void Set(string name, object value, RegistryValueKind kind)
        {
            try { using (var k = Registry.CurrentUser.CreateSubKey(KeyPath)) k.SetValue(name, value, kind); }
            catch { }
        }

        public static int? X { get { return Get("X") as int?; } set { Set("X", value ?? 0, RegistryValueKind.DWord); } }
        public static int? Y { get { return Get("Y") as int?; } set { Set("Y", value ?? 0, RegistryValueKind.DWord); } }

        public static bool UsageAsked
        {
            get { return (Get("UsageAsked") as int?) == 1; }
            set { Set("UsageAsked", value ? 1 : 0, RegistryValueKind.DWord); }
        }

        public static bool UsageEnabled
        {
            get { return (Get("UsageStats") as int?) == 1; }
            set { Set("UsageStats", value ? 1 : 0, RegistryValueKind.DWord); }
        }

        /// <summary>Random ID created on first use. Not derived from the machine.</summary>
        public static string InstallId
        {
            get
            {
                var s = Get("InstallId") as string;
                Guid g;
                if (s != null && Guid.TryParse(s, out g)) return g.ToString();
                s = Guid.NewGuid().ToString();
                Set("InstallId", s, RegistryValueKind.String);
                return s;
            }
        }
    }
}
