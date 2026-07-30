using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>What happens once a GIF recording stops. The file is always written.</summary>
    public enum GifOutput
    {
        SaveOnly = 0,
        SaveAndCopyPath = 1,
        SaveAndOpen = 2
    }

    /// <summary>
    /// Everything about GIF recording: its own hot key, frame rate and length, its own
    /// selection lock, and its own folder. Screen capture keeps a separate set in
    /// Capture Setting, so the two features never have to share a shape or a folder.
    /// </summary>
    public partial class GifSettingsForm : PixelPerfectForm
    {
        private static readonly string[] RatioPresets =
            { "16:9", "16:10", "4:3", "3:2", "1:1", "21:9", "9:16", "3:4" };

        private CheckBox[] _modifiers;
        private RadioButton[] _outputs;

        public GifSettingsForm()
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

            _modifiers = new CheckBox[] { Gif_Alt, Gif_Control, Gif_Shift, Gif_Windows };
            _outputs = new RadioButton[] { outputSave, outputCopyPath, outputOpen };

            Gif_Letter.Text = Properties.Settings.Default.GifHotKey;
            foreach (var mod in _modifiers)
            {
                mod.Checked = (0 != (Properties.Settings.Default.GifHotKeyModifier & int.Parse(mod.Tag.ToString())));
            }
            gifFps.Text = Properties.Settings.Default.GifFps.ToString();
            gifSeconds.Text = Properties.Settings.Default.GifSeconds.ToString();

            foreach (var preset in RatioPresets)
            {
                ratioBox.Items.Add(preset);
            }
            string savedRatio = Properties.Settings.Default.GifRatioPreset;
            if (!string.IsNullOrEmpty(savedRatio) && !ratioBox.Items.Contains(savedRatio))
            {
                ratioBox.Items.Add(savedRatio);
            }
            ratioBox.SelectedItem = string.IsNullOrEmpty(savedRatio) ? "16:9" : savedRatio;
            if (ratioBox.SelectedIndex < 0) ratioBox.SelectedIndex = 0;

            lockRatio.Checked = Properties.Settings.Default.GifLockRatio;
            lockPixel.Checked = Properties.Settings.Default.GifLockPixel;
            pixelWidth.Text = Properties.Settings.Default.GifPixelWidth.ToString();
            pixelHeight.Text = Properties.Settings.Default.GifPixelHeight.ToString();

            foreach (var output in _outputs)
            {
                output.Checked = (Properties.Settings.Default.GifOutput == int.Parse(output.Tag.ToString()));
            }
            folderBox.Text = DefaultFolder();

            SetLockControls();
        }

        /// <summary>GIF folder; falls back to the screen capture folder, then Pictures\MicroApp.</summary>
        public static string DefaultFolder()
        {
            var saved = Properties.Settings.Default.GifFolder;
            if (!string.IsNullOrWhiteSpace(saved)) return saved;
            return CaptureSettingsForm.DefaultFolder();
        }

        private void StyleText()
        {
            titleLabel.Font = Theme.Heading;
            titleLabel.ForeColor = Theme.Text;
            subtitleLabel.Font = Theme.Small;
            subtitleLabel.ForeColor = Theme.TextDim;

            modifiersLabel.Font = new Font(Theme.Small, FontStyle.Bold);
            modifiersLabel.ForeColor = Theme.TextDim;

            foreach (var helper in new[] { keyLabel, fpsLabel, secondsLabel, byLabel, pxLabel, lockNote })
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

        private void Gif_Letter_KeyDown(object sender, KeyEventArgs e)
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
                    Gif_Letter.Text = string.Empty;
                    break;
                default:
                    Gif_Letter.Text = e.KeyCode.ToString();
                    break;
            }
            e.SuppressKeyPress = true;
        }

        private void Browse_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Where should recorded GIFs be saved?";
                dialog.SelectedPath = folderBox.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    folderBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void Save_Click(object sender, EventArgs e)
        {
            var letter = Gif_Letter.Text;
            if (letter.Length == 1) letter = letter.ToUpperInvariant();
            Properties.Settings.Default.GifHotKey = letter;

            int mods = 0;
            foreach (var mod in _modifiers)
            {
                if (mod.Checked) mods |= int.Parse(mod.Tag.ToString());
            }
            Properties.Settings.Default.GifHotKeyModifier = mods;

            int fps, secs, w, h;
            if (int.TryParse(gifFps.Text, out fps)) Properties.Settings.Default.GifFps = Math.Max(1, Math.Min(30, fps));
            if (int.TryParse(gifSeconds.Text, out secs)) Properties.Settings.Default.GifSeconds = Math.Max(1, Math.Min(600, secs));

            Properties.Settings.Default.GifLockRatio = lockRatio.Checked;
            Properties.Settings.Default.GifRatioPreset = ratioBox.SelectedItem != null
                ? ratioBox.SelectedItem.ToString()
                : "16:9";
            Properties.Settings.Default.GifLockPixel = lockPixel.Checked;
            if (int.TryParse(pixelWidth.Text, out w) && w >= 8) Properties.Settings.Default.GifPixelWidth = w;
            if (int.TryParse(pixelHeight.Text, out h) && h >= 8) Properties.Settings.Default.GifPixelHeight = h;

            foreach (var output in _outputs)
            {
                if (output.Checked) Properties.Settings.Default.GifOutput = int.Parse(output.Tag.ToString());
            }
            Properties.Settings.Default.GifFolder = folderBox.Text.Trim();

            Properties.Settings.Default.Save();
        }
    }
}
