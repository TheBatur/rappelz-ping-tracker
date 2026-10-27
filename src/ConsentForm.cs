// One-time notice about the anonymous usage counter, shown on first launch.
using System.Drawing;
using System.Windows.Forms;

namespace RappelzPing
{
    class ConsentForm : Form
    {
        public ConsentForm()
        {
            Text = T.ConsentTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Color.FromArgb(28, 28, 32);
            ForeColor = Color.FromArgb(230, 230, 232);
            Padding = new Padding(18);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
            var text = new Label { Text = T.ConsentText, AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(0, 0, 0, 16) };
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
            var ok = MakeButton(T.ConsentOk, DialogResult.OK, true);
            var no = MakeButton(T.ConsentNo, DialogResult.Cancel, false);
            buttons.Controls.Add(ok);
            buttons.Controls.Add(no);
            layout.Controls.Add(text);
            layout.Controls.Add(buttons);
            Controls.Add(layout);
            AcceptButton = ok;
            CancelButton = no;
        }

        static Button MakeButton(string label, DialogResult result, bool primary)
        {
            var b = new Button
            {
                Text = label, DialogResult = result, AutoSize = true, MinimumSize = new Size(96, 30),
                FlatStyle = FlatStyle.Flat, Margin = new Padding(8, 0, 0, 0),
                BackColor = primary ? Color.FromArgb(46, 125, 70) : Color.FromArgb(48, 48, 54),
                ForeColor = Color.White,
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }
    }
}
