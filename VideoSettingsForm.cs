using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>What happens once a video recording stops. The file is always written.</summary>
    public enum VideoOutput
    {
        SaveOnly = 0,
        SaveAndCopyPath = 1,
        SaveAndOpen = 2
    }

    /// <summary>Where the sound track comes from.</summary>
    public enum VideoAudioSource
    {
        None = 0,
        System = 1,
        Microphone = 2,
        SystemAndMicrophone = 3
    }

    /// <summary>Trades file size against picture quality; drives the H.264 bitrate.</summary>
    public enum VideoQuality
    {
        Small = 0,
        Balanced = 1,
        Sharp = 2
    }

    /// <summary>
    /// Everything about video recording: its own hot key, frame rate and length, the
    /// size/quality trade, the sound source, its own selection lock and its own folder.
    /// GIF and screen capture keep their separate sets, so nothing is shared.
    /// </summary>
    public partial class VideoSettingsForm : PixelPerfectForm
    {
        private static readonly string[] RatioPresets =
            { "16:9", "16:10", "8:5", "4:3", "3:2", "1:1", "21:9", "9:16", "3:4" };

        private CheckBox[] _modifiers;
        private RadioButton[] _outputs;

        // live microphone preview that drives the level meter while the dialog is open
        private AudioCapture _preview;
        private Timer _meterTimer;
        private volatile float _previewLevel;
        private bool _shown;

        public VideoSettingsForm()
        {
            InitializeComponent();
            // taller than a 768-pixel laptop screen: scroll instead of running off it
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            if (Height > work.Height)
            {
                AutoScroll = true;
                Height = work.Height;
                Width += SystemInformation.VerticalScrollBarWidth;
            }

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

            _modifiers = new CheckBox[] { Video_Alt, Video_Control, Video_Shift, Video_Windows };
            _outputs = new RadioButton[] { outputSave, outputCopyPath, outputOpen };

            Video_Letter.Text = Properties.Settings.Default.VideoHotKey;
            foreach (var mod in _modifiers)
            {
                mod.Checked = (0 != (Properties.Settings.Default.VideoHotKeyModifier & int.Parse(mod.Tag.ToString())));
            }
            videoFps.Text = Properties.Settings.Default.VideoFps.ToString();
            // 0 (or less) means unlimited: show it as a blank box
            videoLength.Text = Properties.Settings.Default.VideoSeconds <= 0
                ? string.Empty
                : Properties.Settings.Default.VideoSeconds.ToString();

            qualityBox.DropDownStyle = ComboBoxStyle.DropDownList;
            qualityBox.Items.Add("Small file");
            qualityBox.Items.Add("Balanced");
            qualityBox.Items.Add("Sharp (bigger)");
            qualityBox.SelectedIndex = Math.Max(0, Math.Min(2, Properties.Settings.Default.VideoQuality));

            soundBox.DropDownStyle = ComboBoxStyle.DropDownList;
            soundBox.Items.Add("No sound");
            soundBox.Items.Add("System sound");
            soundBox.Items.Add("Microphone");
            soundBox.Items.Add("System sound + microphone");
            soundBox.SelectedIndex = Math.Max(0, Math.Min(3, Properties.Settings.Default.VideoAudioSource));

            PopulateMics();
            int gain = Math.Max(0, Math.Min(200, Properties.Settings.Default.VideoMicGain));
            gainSlider.Value = gain;
            UpdateGainLabel();

            soundBox.SelectedIndexChanged += (s, e2) => { UpdateMicRowEnabled(); RestartPreview(); };
            micBox.SelectedIndexChanged += (s, e2) => RestartPreview();

            _meterTimer = new Timer { Interval = 40 };
            _meterTimer.Tick += (s, e2) =>
            {
                micMeter.SetLevel(_previewLevel);
                _previewLevel = 0f;
            };

            UpdateMicRowEnabled();

            lockCursorSize.Checked = Properties.Settings.Default.VideoLockCursorSize;
            clickGlow.Checked = Properties.Settings.Default.VideoClickGlow;
            actionLog.Checked = Properties.Settings.Default.VideoActionLog;

            foreach (var preset in RatioPresets)
            {
                ratioBox.Items.Add(preset);
            }
            ratioBox.MaxDropDownItems = 10;   // show the whole list, no scrollbar
            string savedRatio = Properties.Settings.Default.VideoRatioPreset;
            if (!string.IsNullOrEmpty(savedRatio) && !ratioBox.Items.Contains(savedRatio))
            {
                ratioBox.Items.Add(savedRatio);
            }
            ratioBox.SelectedItem = string.IsNullOrEmpty(savedRatio) ? "16:9" : savedRatio;
            if (ratioBox.SelectedIndex < 0) ratioBox.SelectedIndex = 0;

            lockRatio.Checked = Properties.Settings.Default.VideoLockRatio;
            lockPixel.Checked = Properties.Settings.Default.VideoLockPixel;
            pixelWidth.Text = Properties.Settings.Default.VideoPixelWidth.ToString();
            pixelHeight.Text = Properties.Settings.Default.VideoPixelHeight.ToString();

            foreach (var output in _outputs)
            {
                output.Checked = (Properties.Settings.Default.VideoOutput == int.Parse(output.Tag.ToString()));
            }
            folderBox.Text = DefaultFolder();

            SetLockControls();
        }

        /// <summary>Video folder; falls back to Videos\MicroApp.</summary>
        public static string DefaultFolder()
        {
            var saved = Properties.Settings.Default.VideoFolder;
            if (!string.IsNullOrWhiteSpace(saved)) return saved;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "MicroApp");
        }

        private void StyleText()
        {
            titleLabel.Font = Theme.Heading;
            titleLabel.ForeColor = Theme.Text;
            subtitleLabel.Font = Theme.Small;
            subtitleLabel.ForeColor = Theme.TextDim;

            modifiersLabel.Font = new Font(Theme.Small, FontStyle.Bold);
            modifiersLabel.ForeColor = Theme.TextDim;

            foreach (var helper in new[] { keyLabel, fpsLabel, lengthLabel, qualityLabel, soundLabel,
                                           micLabel, levelLabel, gainCaption, gainLabel, byLabel, pxLabel, lockNote })
            {
                helper.Font = Theme.Base;
                helper.ForeColor = Theme.TextDim;
            }
            lockNote.Font = Theme.Small;
        }

        /// <summary>Pixel lock wins over ratio lock, so it greys the ratio row out.</summary>
        private void SetLockControls()
        {
            bool pixel = lockPixel.Checked;

            lockRatio.Enabled = !pixel;
            ratioBox.Enabled = lockRatio.Checked && !pixel;
            ratioHost.Invalidate();

            pixelWidth.Enabled = pixel;
            pixelHeight.Enabled = pixel;
            pixelWidthHost.Invalidate();
            pixelHeightHost.Invalidate();

            lockNote.Text = pixel
                ? "Every recording is exactly this many pixels: the box follows the pointer and one click starts it."
                : lockRatio.Checked
                    ? "The recorded area is locked to this shape; the size is still up to you."
                    : "Nothing is locked: drag any rectangle you like.";
        }

        private void Lock_CheckedChanged(object sender, EventArgs e)
        {
            SetLockControls();
        }

        private void Video_Letter_KeyDown(object sender, KeyEventArgs e)
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
                    Video_Letter.Text = string.Empty;
                    break;
                default:
                    Video_Letter.Text = e.KeyCode.ToString();
                    break;
            }
            e.SuppressKeyPress = true;
        }

        private void Browse_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Where should recorded videos be saved?";
                dialog.SelectedPath = folderBox.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    folderBox.Text = dialog.SelectedPath;
                }
            }
        }

        /// <summary>Fills the device list with "Default microphone" plus every active capture endpoint.</summary>
        private void PopulateMics()
        {
            micBox.DropDownStyle = ComboBoxStyle.DropDownList;
            micBox.Items.Clear();
            micBox.Items.Add(new AudioCapture.AudioDevice { Id = "", Name = "Default microphone" });

            string saved = Properties.Settings.Default.VideoMicDeviceId;
            int selIndex = 0;
            try
            {
                foreach (var d in AudioCapture.ListMicrophones())
                {
                    int idx = micBox.Items.Add(d);
                    if (!string.IsNullOrEmpty(saved) && d.Id == saved) selIndex = idx;
                }
            }
            catch (Exception) { }
            micBox.SelectedIndex = selIndex;
        }

        /// <summary>The WASAPI id of the chosen device, or "" for the system default.</summary>
        private string SelectedMicId()
        {
            if (micBox.SelectedItem is AudioCapture.AudioDevice)
                return ((AudioCapture.AudioDevice)micBox.SelectedItem).Id;
            return string.Empty;
        }

        /// <summary>True when the sound mode records the microphone (Microphone or System + microphone).</summary>
        private bool MicModeSelected()
        {
            return soundBox.SelectedIndex == 2 || soundBox.SelectedIndex == 3;
        }

        private void UpdateMicRowEnabled()
        {
            bool on = MicModeSelected();
            micLabel.Enabled = micBox.Enabled = levelLabel.Enabled = on;
            gainCaption.Enabled = gainLabel.Enabled = gainSlider.Enabled = on;
            micHost.Enabled = on;
            micHost.Invalidate();
            if (!on) micMeter.Reset();
        }

        private void UpdateGainLabel()
        {
            gainLabel.Text = gainSlider.Value + "%  (100% = no change, up to 200% boost)";
        }

        private void GainSlider_Scroll(object sender, EventArgs e)
        {
            UpdateGainLabel();
            // apply the new gain to the running preview at once, so the meter reflects it live
            var preview = _preview;
            if (preview != null) preview.Gain = gainSlider.Value / 100f;
        }

        /// <summary>Opens the chosen mic and feeds its peak level to the meter; a no-op unless a mic mode is picked.</summary>
        private void StartPreview()
        {
            StopPreview();
            if (!MicModeSelected()) return;
            try
            {
                _preview = AudioCapture.TryCreate(false, 0, false, SelectedMicId(), gainSlider.Value);
                if (_preview != null)
                {
                    _preview.Start((bytes, n) =>
                    {
                        float peak = 0f;
                        for (int i = 0; i + 1 < n; i += 2)
                        {
                            short s = (short)(bytes[i] | (bytes[i + 1] << 8));
                            float a = Math.Abs((int)s) / 32768f;
                            if (a > peak) peak = a;
                        }
                        if (peak > _previewLevel) _previewLevel = peak;
                    });
                    if (_meterTimer != null) _meterTimer.Start();
                }
            }
            catch (Exception)
            {
                _preview = null;   // device busy or gone: the meter just stays at rest
            }
        }

        private void StopPreview()
        {
            if (_meterTimer != null) _meterTimer.Stop();
            var preview = _preview;
            _preview = null;
            if (preview != null)
            {
                try { preview.Stop(); preview.Dispose(); } catch (Exception) { }
            }
            _previewLevel = 0f;
            if (micMeter != null) micMeter.Reset();
        }

        private void RestartPreview()
        {
            if (_shown) StartPreview();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _shown = true;
            StartPreview();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopPreview();
            base.OnFormClosing(e);
        }

        private void Save_Click(object sender, EventArgs e)
        {
            var letter = Video_Letter.Text;
            if (letter.Length == 1) letter = letter.ToUpperInvariant();
            Properties.Settings.Default.VideoHotKey = letter;

            int mods = 0;
            foreach (var mod in _modifiers)
            {
                if (mod.Checked) mods |= int.Parse(mod.Tag.ToString());
            }
            Properties.Settings.Default.VideoHotKeyModifier = mods;

            int fps, w, h;
            if (int.TryParse(videoFps.Text, out fps)) Properties.Settings.Default.VideoFps = Math.Max(1, Math.Min(30, fps));

            // blank, 0 or an unparseable length all mean "unlimited" (record until stopped)
            int secs;
            string lenText = videoLength.Text.Trim();
            if (lenText.Length == 0 || !int.TryParse(lenText, out secs) || secs <= 0)
                Properties.Settings.Default.VideoSeconds = 0;
            else
                Properties.Settings.Default.VideoSeconds = Math.Max(1, Math.Min(3600, secs));

            Properties.Settings.Default.VideoQuality = qualityBox.SelectedIndex;
            Properties.Settings.Default.VideoAudioSource = soundBox.SelectedIndex;
            Properties.Settings.Default.VideoMicDeviceId = SelectedMicId();
            Properties.Settings.Default.VideoMicGain = Math.Max(0, Math.Min(200, gainSlider.Value));
            Properties.Settings.Default.VideoLockCursorSize = lockCursorSize.Checked;
            Properties.Settings.Default.VideoClickGlow = clickGlow.Checked;
            Properties.Settings.Default.VideoActionLog = actionLog.Checked;

            Properties.Settings.Default.VideoLockRatio = lockRatio.Checked;
            Properties.Settings.Default.VideoRatioPreset = ratioBox.SelectedItem != null
                ? ratioBox.SelectedItem.ToString()
                : "16:9";
            Properties.Settings.Default.VideoLockPixel = lockPixel.Checked;
            if (int.TryParse(pixelWidth.Text, out w) && w >= 8) Properties.Settings.Default.VideoPixelWidth = w;
            if (int.TryParse(pixelHeight.Text, out h) && h >= 8) Properties.Settings.Default.VideoPixelHeight = h;

            foreach (var output in _outputs)
            {
                if (output.Checked) Properties.Settings.Default.VideoOutput = int.Parse(output.Tag.ToString());
            }
            Properties.Settings.Default.VideoFolder = folderBox.Text.Trim();

            Properties.Settings.Default.Save();
        }
    }
}
