// The small always-on-top, draggable ping box.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RappelzPing
{
    class Overlay : Form
    {
        static readonly Color Back = Color.FromArgb(18, 18, 20);
        static readonly Color Green = Color.FromArgb(80, 220, 110);
        static readonly Color Yellow = Color.FromArgb(240, 200, 60);
        static readonly Color Red = Color.FromArgb(240, 80, 70);
        static readonly Color Gray = Color.FromArgb(150, 150, 155);

        readonly PingMeter meter = new PingMeter();
        readonly Usage usage = new Usage(Settings.InstallId);
        readonly Timer timer = new Timer();
        readonly ToolTip tip = new ToolTip();
        readonly Font font = new Font("Segoe UI Semibold", 11f, FontStyle.Regular, GraphicsUnit.Point);
        readonly ToolStripMenuItem usageItem;
        string text = "…";
        Color color = Gray;
        Point dragStart;
        bool dragging;

        public Overlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Back;
            Opacity = 0.88;
            DoubleBuffered = true;
            Text = "Rappelz Ping";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Size widest = Size.Empty;
            foreach (var s in new[] { "8888 ms", T.NoGame, T.NotConnected, T.Measuring, T.NoAdmin })
            {
                var m = TextRenderer.MeasureText(s, font);
                widest = new Size(Math.Max(widest.Width, m.Width), Math.Max(widest.Height, m.Height));
            }
            ClientSize = new Size(widest.Width + 10, widest.Height + 4);
            Region = Rounded(ClientSize, 6);
            Location = SavedLocation();

            var menu = new ContextMenuStrip();
            menu.Items.Add(T.MenuReset, null, (s, e) => { Location = DefaultLocation(); SaveLocation(); });
            usageItem = new ToolStripMenuItem(T.MenuUsage) { Checked = Settings.UsageEnabled, CheckOnClick = true };
            usageItem.CheckedChanged += (s, e) => Settings.UsageEnabled = usageItem.Checked;
            menu.Items.Add(usageItem);
            menu.Items.Add(T.MenuAbout + "  (v" + Program.Version + ")", null, (s, e) =>
            {
                try { Process.Start(Program.RepoUrl); } catch { }
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(T.MenuClose, null, (s, e) => Close());
            ContextMenuStrip = menu;

            tip.InitialDelay = 300;
            timer.Interval = 1000;
            timer.Tick += (s, e) => UpdateView();
            timer.Start();
            UpdateView();
        }

        // Clicking the box must not take focus from the game; keep it out of Alt+Tab.
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x08000000 | 0x8; // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST
                return cp;
            }
        }

        void UpdateView()
        {
            try { meter.Tick(); } catch { }
            string tipText;
            switch (meter.State)
            {
                case MeterState.NoGame: text = T.NoGame; color = Gray; tipText = T.TipNoGame; break;
                case MeterState.NotConnected: text = T.NotConnected; color = Gray; tipText = T.TipNotConnected; break;
                case MeterState.Measuring: text = T.Measuring; color = Gray; tipText = T.TipMeasuring; break;
                case MeterState.NoAdmin: text = T.NoAdmin; color = Red; tipText = T.TipNoAdmin; break;
                default:
                    text = meter.Ms + " ms";
                    color = meter.Ms < 100 ? Green : meter.Ms < 200 ? Yellow : Red;
                    tipText = T.TipServer + meter.Server;
                    break;
            }
            if (meter.Fresh) usage.AddSample(meter.Ms, meter.Server);
            usage.Tick(Settings.UsageEnabled);

            if (tip.GetToolTip(this) != tipText) tip.SetToolTip(this, tipText);
            // Some games push themselves to the top when focused; re-assert every second.
            SetWindowPos(Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // NOSIZE|NOMOVE|NOACTIVATE
            Invalidate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            timer.Stop();
            usage.Final(Settings.UsageEnabled);
            base.OnFormClosing(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            TextRenderer.DrawText(e.Graphics, text, font, ClientRectangle, color, Back,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { dragging = true; dragStart = e.Location; }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging) Location = new Point(Location.X + e.X - dragStart.X, Location.Y + e.Y - dragStart.Y);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (dragging) { dragging = false; SaveLocation(); }
            base.OnMouseUp(e);
        }

        Point DefaultLocation()
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            return new Point(area.Right - Width - 20, area.Top + 20);
        }

        Point SavedLocation()
        {
            int? x = Settings.X, y = Settings.Y;
            if (x.HasValue && y.HasValue)
            {
                var p = new Point(x.Value, y.Value);
                // Monitors may have changed since last time; never open off-screen.
                foreach (var s in Screen.AllScreens)
                    if (s.WorkingArea.IntersectsWith(new Rectangle(p, Size))) return p;
            }
            return DefaultLocation();
        }

        void SaveLocation() { Settings.X = Location.X; Settings.Y = Location.Y; }

        static Region Rounded(Size s, int r)
        {
            var path = new GraphicsPath();
            int d = r * 2;
            path.AddArc(0, 0, d, d, 180, 90);
            path.AddArc(s.Width - d, 0, d, d, 270, 90);
            path.AddArc(s.Width - d, s.Height - d, d, d, 0, 90);
            path.AddArc(0, s.Height - d, d, d, 90, 90);
            path.CloseFigure();
            return new Region(path);
        }

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}
