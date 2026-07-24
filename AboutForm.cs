using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// About box: who wrote it, how to reach them, and what the app is built on.
    /// Same 640 x 612 canvas as the settings windows.
    /// </summary>
    public class AboutForm : Form
    {
        public const string AuthorName = "Samsur Rahman Mahi";
        public const string AuthorEmail = "mahi@rampsbd.com";

        private readonly HeaderBar _header;
        private readonly PictureBox _icon;
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly Pill _version;

        private readonly Card _cardApp;
        private readonly Label _appText;

        private readonly Card _cardAuthor;
        private readonly Label _authorName;
        private readonly LinkLabel _authorEmail;
        private readonly ModernButton _copyEmail;

        private readonly Card _cardCredits;
        private readonly Label _creditsText;

        private readonly ModernButton _closeButton;

        public AboutForm()
        {
            Theme.Init(ThemeHelper.IsDarkMode);
            SuspendLayout();

            // size first: the header takes its width from the form, and the version
            // pill is positioned against that width
            ClientSize = new Size(640, 612);

            var version = Assembly.GetExecutingAssembly().GetName().Version;

            // width matters before children go in: anchoring resolves against it
            _header = new HeaderBar { Dock = DockStyle.Top, Width = ClientSize.Width, Height = 84, TabStop = false };
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
                Text = "About MicroApp",
                BackColor = Color.Transparent
            };
            _subtitle = new Label
            {
                AutoSize = true,
                Location = new Point(78, 47),
                Text = "Clipboard typing, screen text, screenshots and GIFs",
                BackColor = Color.Transparent
            };
            _version = new Pill
            {
                Location = new Point(548, 30),
                Size = new Size(68, 24),
                Text = $"v{version.Major}.{version.Minor}.{version.Build}",
                BackColor = Color.Transparent
            };
            _header.Controls.Add(_icon);
            _header.Controls.Add(_title);
            _header.Controls.Add(_subtitle);
            _header.Controls.Add(_version);

            //
            // what it does
            //
            _cardApp = new Card
            {
                Location = new Point(24, 104),
                Size = new Size(592, 148),
                Title = "MicroApp",
                Description = "A tray tool that types, reads and records the screen"
            };
            _appText = new Label
            {
                Location = new Point(16, 58),
                Size = new Size(560, 76),
                Text =
                    "Type the clipboard as real keystrokes into any window.\r\n" +
                    "Read text off the screen with OCR, from a browser, an image, anywhere.\r\n" +
                    "Capture a region as a PNG, or record it as an animated GIF."
            };
            _cardApp.Controls.Add(_appText);

            //
            // who made it
            //
            _cardAuthor = new Card
            {
                Location = new Point(24, 268),
                Size = new Size(592, 148),
                Title = "Author",
                Description = "Questions, bugs and ideas are welcome"
            };
            _authorName = new Label
            {
                AutoSize = true,
                Location = new Point(16, 60),
                Text = AuthorName
            };
            _authorEmail = new LinkLabel
            {
                AutoSize = true,
                Location = new Point(16, 90),
                Text = AuthorEmail,
                LinkBehavior = LinkBehavior.HoverUnderline
            };
            _authorEmail.LinkClicked += (s, e) => OpenMail();
            _copyEmail = new ModernButton
            {
                Location = new Point(452, 84),
                Size = new Size(124, 32),
                Text = "Copy email"
            };
            _copyEmail.Click += (s, e) =>
            {
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try { Clipboard.SetText(AuthorEmail); break; }
                    catch (System.Runtime.InteropServices.ExternalException) { System.Threading.Thread.Sleep(80); }
                }
                Toast.Show("Email address copied.");
            };
            _cardAuthor.Controls.Add(_authorName);
            _cardAuthor.Controls.Add(_authorEmail);
            _cardAuthor.Controls.Add(_copyEmail);

            //
            // what it stands on
            //
            _cardCredits = new Card
            {
                Location = new Point(24, 432),
                Size = new Size(592, 120),
                Title = "Built with",
                Description = "Open source pieces this app relies on"
            };
            _creditsText = new Label
            {
                Location = new Point(16, 58),
                Size = new Size(560, 52),
                Text =
                    "Windows OCR (Windows.Media.Ocr) - AutoItX - MouseKeyHook\r\n" +
                    "Based on ClickPaste by Collective Software LLC, BSD 3-Clause."
            };
            _cardCredits.Controls.Add(_creditsText);

            _closeButton = new ModernButton
            {
                Location = new Point(536, 564),
                Size = new Size(80, 36),
                Text = "Close",
                Accent = true,
                DialogResult = DialogResult.OK,
                TabIndex = 0
            };

            Controls.Add(_cardApp);
            Controls.Add(_cardAuthor);
            Controls.Add(_cardCredits);
            Controls.Add(_closeButton);
            Controls.Add(_header);

            AcceptButton = _closeButton;
            CancelButton = _closeButton;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            SizeGripStyle = SizeGripStyle.Hide;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "About MicroApp";
            Icon = Properties.Resources.AppIcon;

            Theme.Apply(this);
            StyleText();

            ResumeLayout(false);
        }

        private void StyleText()
        {
            _title.Font = Theme.Heading;
            _title.ForeColor = Theme.Text;
            _subtitle.Font = Theme.Small;
            _subtitle.ForeColor = Theme.TextDim;

            _appText.Font = Theme.Base;
            _appText.ForeColor = Theme.TextDim;

            _authorName.Font = new Font("Segoe UI Semibold", 11.25F);
            _authorName.ForeColor = Theme.Text;

            _authorEmail.Font = Theme.Base;
            _authorEmail.LinkColor = Theme.Accent;
            _authorEmail.ActiveLinkColor = Theme.AccentPressed;
            _authorEmail.VisitedLinkColor = Theme.Accent;
            _authorEmail.BackColor = Color.Transparent;

            _creditsText.Font = Theme.Small;
            _creditsText.ForeColor = Theme.TextDim;
        }

        private void OpenMail()
        {
            try
            {
                Process.Start("mailto:" + AuthorEmail);
            }
            catch (Exception)
            {
                // no mail client registered: the address is still on screen and copyable
                Toast.Show("No mail app is set up. Use Copy email instead.");
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetDarkModeForWindow(Handle, Theme.Dark);
            Theme.RoundWindowCorners(Handle);
        }
    }
}
