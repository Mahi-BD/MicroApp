using System;
using System.Drawing;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// Preview of what OCR read, with the option to copy it or type it out.
    /// Built in code (no designer) from the same control set as the settings window.
    /// </summary>
    public class OcrResultForm : Form
    {
        private readonly HeaderBar _header;
        private readonly PictureBox _icon;
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly FieldHost _textHost;
        private readonly TextBox _text;
        private readonly ModernButton _copy;
        private readonly ModernButton _type;
        private readonly ModernButton _close;

        /// <summary>Set when the user asked for the text to be typed out.</summary>
        public bool TypeRequested { get; private set; }

        /// <summary>The text as shown -- the user may have edited it before copying.</summary>
        public string CapturedText { get { return _text.Text; } }

        public OcrResultForm(string text)
        {
            Theme.Init(ThemeHelper.IsDarkMode);
            SuspendLayout();

            _header = new HeaderBar { Dock = DockStyle.Top, Height = 84, TabStop = false };
            _icon = new PictureBox
            {
                Location = new Point(24, 22),
                Size = new Size(40, 40),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                TabStop = false
            };
            using (var large = new Icon(Properties.Resources.AppIcon, 40, 40))
            {
                _icon.Image = large.ToBitmap();
            }

            _title = new Label
            {
                AutoSize = true,
                Location = new Point(76, 19),
                Text = "Text captured",
                BackColor = Color.Transparent
            };
            int chars = (text ?? string.Empty).Length;
            _subtitle = new Label
            {
                AutoSize = true,
                Location = new Point(78, 47),
                Text = chars == 0
                    ? "Nothing readable in that selection"
                    : $"{chars:N0} characters. Edit if you like, then copy or type.",
                BackColor = Color.Transparent
            };

            _header.Controls.Add(_icon);
            _header.Controls.Add(_title);
            _header.Controls.Add(_subtitle);

            _text = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Text = text ?? string.Empty,
                TabIndex = 0
            };
            _textHost = new FieldHost { Location = new Point(24, 104), Size = new Size(552, 280) };
            _textHost.Controls.Add(_text);

            _close = new ModernButton
            {
                Text = "Close",
                Size = new Size(88, 36),
                Location = new Point(300, 400),
                DialogResult = DialogResult.Cancel,
                TabIndex = 3
            };
            _type = new ModernButton
            {
                Text = "Type it out",
                Size = new Size(110, 36),
                Location = new Point(396, 400),
                TabIndex = 2
            };
            _copy = new ModernButton
            {
                Text = "Copy",
                Size = new Size(82, 36),
                Location = new Point(514, 400),
                Accent = true,
                TabIndex = 1
            };

            _copy.Click += (s, e) =>
            {
                CopyToClipboard();
                DialogResult = DialogResult.OK;
                Close();
            };
            _type.Click += (s, e) =>
            {
                TypeRequested = true;
                DialogResult = DialogResult.Yes;
                Close();
            };

            Controls.Add(_textHost);
            Controls.Add(_close);
            Controls.Add(_type);
            Controls.Add(_copy);
            Controls.Add(_header);

            AcceptButton = _copy;
            CancelButton = _close;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(600, 452);
            Font = new Font("Segoe UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "MicroApp OCR";
            Icon = Properties.Resources.AppIcon;
            TopMost = true;

            Theme.Apply(this);
            _title.Font = Theme.Heading;
            _title.ForeColor = Theme.Text;
            _subtitle.Font = Theme.Small;
            _subtitle.ForeColor = Theme.TextDim;
            _text.Font = new Font("Segoe UI", 9.75F);

            ResumeLayout(false);
        }

        /// <summary>Clipboard writes fail while another app holds it; retry briefly.</summary>
        public void CopyToClipboard()
        {
            if (string.IsNullOrEmpty(_text.Text)) return;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Clipboard.SetText(_text.Text);
                    return;
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    System.Threading.Thread.Sleep(80);
                }
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetDarkModeForWindow(Handle, Theme.Dark);
            Theme.RoundWindowCorners(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _text.SelectionStart = 0;
            _text.SelectionLength = 0;
            Activate();
        }
    }
}
