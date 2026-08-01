using System;
using System.Drawing;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// Everything about notes: the hot key, the three date/time insert formats (live
    /// preview under each), and the AI service the Grammar button talks to.
    /// </summary>
    public partial class NoteSettingsForm : PixelPerfectForm
    {
        private CheckBox[] _modifiers;
        private int _lastProvider = -1;

        public NoteSettingsForm()
        {
            InitializeComponent();

            bool dark = ThemeHelper.IsDarkMode;
            Theme.Init(dark);
            Theme.Apply(this);
            StyleText();
            Native.SetDarkModeForWindow(this.Handle, dark);
            Theme.RoundWindowCorners(this.Handle);

            this.Icon = Properties.Resources.AppIcon;
            using (var large = new Icon(Properties.Resources.AppIcon, 40, 40))
            {
                iconBox.Image = large.ToBitmap();
            }

            _modifiers = new CheckBox[] { Note_Alt, Note_Control, Note_Shift, Note_Windows };

            Note_Letter.Text = Properties.Settings.Default.NoteHotKey;
            foreach (var mod in _modifiers)
            {
                mod.Checked = (0 != (Properties.Settings.Default.NoteHotKeyModifier & int.Parse(mod.Tag.ToString())));
            }

            hideTaskbarCheck.Checked = Properties.Settings.Default.NoteHideTaskbar;

            dateBox.Text = Properties.Settings.Default.NoteDateFormat;
            longDateBox.Text = Properties.Settings.Default.NoteLongDateFormat;
            timeBox.Text = Properties.Settings.Default.NoteTimestampFormat;

            providerBox.DropDownStyle = ComboBoxStyle.DropDownList;
            providerBox.Items.Add("MiMo");
            providerBox.Items.Add("Gemini");
            providerBox.Items.Add("ChatGPT");
            providerBox.Items.Add("OpenRouter");
            int provider = Math.Max(0, Math.Min(3, Properties.Settings.Default.NoteAiProvider));
            _lastProvider = provider;
            providerBox.SelectedIndex = provider;
            modelBox.Text = Properties.Settings.Default.NoteAiModel;
            if (modelBox.Text.Trim().Length == 0) modelBox.Text = NoteAi.DefaultModel(provider);
            if (provider == NoteAi.ProviderMiMo && modelBox.Text.Trim() == "mimo")
            {
                modelBox.Text = NoteAi.DefaultModel(provider);   // pre-V2.5 default, no longer served
            }
            baseUrlBox.Text = Properties.Settings.Default.NoteAiBaseUrl;
            if (baseUrlBox.Text.Trim().Length == 0) baseUrlBox.Text = NoteAi.DefaultMiMoBaseUrl;
            baseUrlBox.Enabled = provider == NoteAi.ProviderMiMo;
            apiKeyBox.Text = Properties.Settings.Default.NoteAiApiKey;
            banglaTokenBox.Text = Properties.Settings.Default.NoteBanglaToken;

            ShowSyncState();
            ShowPreviews();
        }

        private void StyleText()
        {
            titleLabel.Font = Theme.Heading;
            titleLabel.ForeColor = Theme.Text;
            subtitleLabel.Font = Theme.Small;
            subtitleLabel.ForeColor = Theme.TextDim;

            foreach (var caption in new[] { modifiersLabel, providerLabel, modelLabel,
                                           baseUrlLabel, apiKeyLabel, banglaLabel })
            {
                caption.Font = new Font(Theme.Small, FontStyle.Bold);
                caption.ForeColor = Theme.TextDim;
            }

            foreach (var helper in new[] { keyLabel, dateLabel, longDateLabel, timeLabel, syncStatusLabel })
            {
                helper.Font = Theme.Base;
                helper.ForeColor = Theme.TextDim;
            }
        }

        /// <summary>Each format label doubles as a live preview of today's date in it.</summary>
        private void ShowPreviews()
        {
            dateLabel.Text = "date — " + Preview(dateBox.Text, "yyyy-MM-dd");
            longDateLabel.Text = "long date — " + Preview(longDateBox.Text, "dddd, dd MMMM yyyy");
            timeLabel.Text = "timestamp — " + Preview(timeBox.Text, "yyyy-MM-dd HH:mm:ss");
        }

        private static string Preview(string format, string fallback)
        {
            if (string.IsNullOrWhiteSpace(format)) format = fallback;
            try { return DateTime.Now.ToString(format); }
            catch (Exception) { return "invalid format"; }
        }

        private void Format_TextChanged(object sender, EventArgs e)
        {
            ShowPreviews();
        }

        /// <summary>Switching providers swaps in that provider's stock model, unless the user typed their own.</summary>
        private void Provider_Changed(object sender, EventArgs e)
        {
            string current = modelBox.Text.Trim();
            if (current.Length == 0 || (_lastProvider >= 0 && current == NoteAi.DefaultModel(_lastProvider)))
            {
                modelBox.Text = NoteAi.DefaultModel(providerBox.SelectedIndex);
            }
            _lastProvider = providerBox.SelectedIndex;
            baseUrlBox.Enabled = providerBox.SelectedIndex == NoteAi.ProviderMiMo;
        }

        private void Note_Letter_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Alt:
                case Keys.Menu:
                case Keys.LMenu:
                case Keys.RMenu:
                case Keys.Shift:
                case Keys.ShiftKey:
                case Keys.LShiftKey:
                case Keys.RShiftKey:
                case Keys.Control:
                case Keys.ControlKey:
                case Keys.LControlKey:
                case Keys.RControlKey:
                case Keys.LWin:
                case Keys.RWin:
                case Keys.Return:
                    break;
                case Keys.Delete:
                case Keys.Back:
                    Note_Letter.Text = string.Empty;
                    break;
                default:
                    Note_Letter.Text = e.KeyCode.ToString();
                    break;
            }
            e.SuppressKeyPress = true;
        }

        /// <summary>
        /// One line saying whether sync is on and what it last did, plus the way in.
        /// Everything else about sync lives in the wizard.
        /// </summary>
        private void ShowSyncState()
        {
            bool on = NoteCloud.IsOn;
            syncButton.Text = on ? "Sync now" : "Set up sync";
            syncCodeButton.Visible = on;
            disconnectButton.Visible = on;
            syncStatusLabel.Text = on
                ? (NoteCloud.Status.Length > 0 ? NoteCloud.Status : "On - keeping these notes in your database.")
                : "Off - the notes are plain .txt files on this PC, and nothing leaves it.";
        }

        private void Sync_Click(object sender, EventArgs e)
        {
            if (!NoteCloud.IsOn)
            {
                NoteSyncWizardForm.Run(this);
                ShowSyncState();
                return;
            }

            syncButton.Enabled = false;
            syncStatusLabel.Text = "Syncing...";
            Cursor = Cursors.WaitCursor;
            try
            {
                string trouble = NoteCloud.SyncNow();
                syncStatusLabel.Text = trouble ?? NoteCloud.Status;
            }
            finally
            {
                Cursor = Cursors.Default;
                syncButton.Enabled = true;
            }
        }

        /// <summary>Shows the code again so a third PC can be added later.</summary>
        private void SyncCode_Click(object sender, EventArgs e)
        {
            NoteSyncWizardForm.Run(this);
            ShowSyncState();
        }

        private void Disconnect_Click(object sender, EventArgs e)
        {
            if (!ModernDialog.Confirm("Stop syncing",
                "The notes already on this PC stay put, and the ones in your database stay there too.",
                "Stop syncing", "Keep syncing")) return;
            NoteCloud.Disconnect();
            ShowSyncState();
        }

        private void Save_Click(object sender, EventArgs e)
        {
            var letter = Note_Letter.Text;
            if (letter.Length == 1) letter = letter.ToUpperInvariant();
            Properties.Settings.Default.NoteHotKey = letter;

            int mods = 0;
            foreach (var mod in _modifiers)
            {
                if (mod.Checked) mods |= int.Parse(mod.Tag.ToString());
            }
            Properties.Settings.Default.NoteHotKeyModifier = mods;

            Properties.Settings.Default.NoteDateFormat = dateBox.Text.Trim();
            Properties.Settings.Default.NoteLongDateFormat = longDateBox.Text.Trim();
            Properties.Settings.Default.NoteTimestampFormat = timeBox.Text.Trim();

            Properties.Settings.Default.NoteHideTaskbar = hideTaskbarCheck.Checked;

            Properties.Settings.Default.NoteAiProvider = providerBox.SelectedIndex;
            Properties.Settings.Default.NoteAiModel = modelBox.Text.Trim();
            Properties.Settings.Default.NoteAiBaseUrl = baseUrlBox.Text.Trim();
            Properties.Settings.Default.NoteAiApiKey = apiKeyBox.Text.Trim();
            Properties.Settings.Default.NoteBanglaToken = banglaTokenBox.Text.Trim();

            Properties.Settings.Default.Save();
            NoteForm.ApplyTaskbarSetting();   // open notes follow the toggle right away
        }
    }
}
