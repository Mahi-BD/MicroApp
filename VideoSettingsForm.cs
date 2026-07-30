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
        Microphone = 2
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
    public partial class VideoSettingsForm : Form
    {
        private static readonly string[] RatioPresets =
            { "16:9", "16:10", "4:3", "3:2", "1:1", "21:9", "9:16", "3:4" };

        private CheckBox[] _modifiers;
        private RadioButton[] _outputs;

        public VideoSettingsForm()
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

            _modifiers = new CheckBox[] { Video_Alt, Video_Control, Video_Shift, Video_Windows };
            _outputs = new RadioButton[] { outputSave, outputCopyPath, outputOpen };

            Video_Letter.Text = Properties.Settings.Default.VideoHotKey;
            foreach (var mod in _modifiers)
            {
                mod.Checked = (0 != (Properties.Settings.Default.VideoHotKeyModifier & int.Parse(mod.Tag.ToString())));
            }
            videoFps.Text = Properties.Settings.Default.VideoFps.ToString();

            qualityBox.DropDownStyle = ComboBoxStyle.DropDownList;
            qualityBox.Items.Add("Small file");
            qualityBox.Items.Add("Balanced");
            qualityBox.Items.Add("Sharp (bigger)");
            qualityBox.SelectedIndex = Math.Max(0, Math.Min(2, Properties.Settings.Default.VideoQuality));

            soundBox.DropDownStyle = ComboBoxStyle.DropDownList;
            soundBox.Items.Add("No sound");
            soundBox.Items.Add("System sound");
            soundBox.Items.Add("Microphone");
            soundBox.SelectedIndex = Math.Max(0, Math.Min(2, Properties.Settings.Default.VideoAudioSource));

            foreach (var preset in RatioPresets)
            {
                ratioBox.Items.Add(preset);
            }
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

            foreach (var helper in new[] { keyLabel, fpsLabel, qualityLabel, soundLabel, byLabel, pxLabel, lockNote })
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

            Properties.Settings.Default.VideoQuality = qualityBox.SelectedIndex;
            Properties.Settings.Default.VideoAudioSource = soundBox.SelectedIndex;

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
