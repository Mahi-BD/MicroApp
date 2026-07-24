using System;
using System.Drawing;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// Themed replacement for MessageBox: same two-button flow, but it matches the
    /// rest of the app (dark title bar, card typography, accent primary button).
    /// </summary>
    public class ModernDialog : Form
    {
        private readonly PictureBox _icon;
        private readonly Label _heading;
        private readonly Label _body;
        private readonly ModernButton _primary;
        private readonly ModernButton _secondary;

        private ModernDialog(string heading, string body, string primaryText, string secondaryText)
        {
            Theme.Init(ThemeHelper.IsDarkMode);

            SuspendLayout();

            _icon = new PictureBox
            {
                Location = new Point(28, 28),
                Size = new Size(40, 40),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                TabStop = false
            };
            using (var large = new Icon(Properties.Resources.AppIcon, 40, 40))
            {
                _icon.Image = large.ToBitmap();
            }

            _heading = new Label
            {
                Location = new Point(84, 26),
                Size = new Size(368, 26),
                Text = heading,
                Font = new Font("Segoe UI Semibold", 11.25F),
                ForeColor = Theme.Text,
                BackColor = Color.Transparent
            };

            _body = new Label
            {
                Location = new Point(86, 56),
                Size = new Size(368, 76),
                Text = body,
                Font = Theme.Base,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent
            };

            _secondary = new ModernButton
            {
                Size = new Size(96, 36),
                Text = secondaryText,
                DialogResult = DialogResult.No,
                TabIndex = 1
            };

            _primary = new ModernButton
            {
                Size = new Size(112, 36),
                Text = primaryText,
                Accent = true,
                DialogResult = DialogResult.Yes,
                TabIndex = 0
            };

            bool twoButtons = !string.IsNullOrEmpty(secondaryText);
            ClientSize = new Size(484, 196);
            _primary.Location = new Point(ClientSize.Width - 24 - _primary.Width, 140);
            _secondary.Location = new Point(_primary.Left - 12 - _secondary.Width, 140);
            _secondary.Visible = twoButtons;

            Controls.Add(_icon);
            Controls.Add(_heading);
            Controls.Add(_body);
            Controls.Add(_primary);
            if (twoButtons) Controls.Add(_secondary);

            AcceptButton = _primary;
            CancelButton = twoButtons ? _secondary : _primary;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "MicroApp";
            Icon = Properties.Resources.AppIcon;
            Font = Theme.Base;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            TopMost = true;

            ResumeLayout(false);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetDarkModeForWindow(Handle, Theme.Dark);
            Theme.RoundWindowCorners(Handle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // hairline above the button row, same idiom as the settings footer
            using (var pen = new Pen(Theme.Border))
            {
                e.Graphics.DrawLine(pen, 0, 124, ClientSize.Width, 124);
            }
            using (var brush = new SolidBrush(Theme.Surface))
            {
                e.Graphics.FillRectangle(brush, 0, 125, ClientSize.Width, ClientSize.Height - 125);
            }
            _primary.BackColor = Theme.Surface;
            _secondary.BackColor = Theme.Surface;
        }

        /// <summary>Yes/No question. Returns true when the primary button was chosen.</summary>
        public static bool Confirm(string heading, string body, string primaryText, string secondaryText)
        {
            using (var dialog = new ModernDialog(heading, body, primaryText, secondaryText))
            {
                return dialog.ShowDialog() == DialogResult.Yes;
            }
        }

        /// <summary>Single-button notice.</summary>
        public static void Info(string heading, string body)
        {
            using (var dialog = new ModernDialog(heading, body, "OK", null))
            {
                dialog.ShowDialog();
            }
        }
    }
}
