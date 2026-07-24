using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>Where a screen capture goes once it is taken.</summary>
    public enum CaptureOutput
    {
        Clipboard = 0,
        File = 1,
        Both = 2
    }

    /// <summary>
    /// Screen capture only: its hot key, its selection lock and its output. GIF
    /// recording keeps a separate set of all three in GIF Setting.
    /// </summary>
    public partial class CaptureSettingsForm : Form
    {
        private static readonly string[] RatioPresets =
            { "16:9", "16:10", "4:3", "3:2", "1:1", "21:9", "9:16", "3:4" };

        private CheckBox[] _modifiers;
        private RadioButton[] _outputs;

        public CaptureSettingsForm()
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

            _modifiers = new CheckBox[] { Cap_Alt, Cap_Control, Cap_Shift, Cap_Windows };
            _outputs = new RadioButton[] { outputClipboard, outputFile, outputBoth };

            Cap_Letter.Text = Properties.Settings.Default.CaptureHotKey;
            foreach (var mod in _modifiers)
            {
                mod.Checked = (0 != (Properties.Settings.Default.CaptureHotKeyModifier & int.Parse(mod.Tag.ToString())));
            }

            foreach (var preset in RatioPresets)
            {
                ratioBox.Items.Add(preset);
            }
            string savedRatio = Properties.Settings.Default.RatioPreset;
            if (!string.IsNullOrEmpty(savedRatio) && !ratioBox.Items.Contains(savedRatio))
            {
                ratioBox.Items.Add(savedRatio);
            }
            ratioBox.SelectedItem = string.IsNullOrEmpty(savedRatio) ? "16:9" : savedRatio;
            if (ratioBox.SelectedIndex < 0) ratioBox.SelectedIndex = 0;

            lockRatio.Checked = Properties.Settings.Default.LockRatio;
            lockPixel.Checked = Properties.Settings.Default.LockPixel;
            pixelWidth.Text = Properties.Settings.Default.PixelWidth.ToString();
            pixelHeight.Text = Properties.Settings.Default.PixelHeight.ToString();

            foreach (var output in _outputs)
            {
                output.Checked = (Properties.Settings.Default.CaptureOutput == int.Parse(output.Tag.ToString()));
            }
            folderBox.Text = DefaultFolder();

            SetLockControls();
        }

        /// <summary>Saved folder, or Pictures\MicroApp when nothing is configured yet.</summary>
        public static string DefaultFolder()
        {
            var saved = Properties.Settings.Default.CaptureFolder;
            if (!string.IsNullOrWhiteSpace(saved)) return saved;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "MicroApp");
        }

        private void StyleText()
        {
            titleLabel.Font = Theme.Heading;
            titleLabel.ForeColor = Theme.Text;
            subtitleLabel.Font = Theme.Small;
            subtitleLabel.ForeColor = Theme.TextDim;

            modifiersLabel.Font = new Font(Theme.Small, FontStyle.Bold);
            modifiersLabel.ForeColor = Theme.TextDim;

            foreach (var helper in new[] { keyLabel, byLabel, pxLabel, lockNote, folderLabel })
            {
                helper.Font = Theme.Base;
                helper.ForeColor = Theme.TextDim;
            }
            lockNote.Font = Theme.Small;
            folderLabel.Font = new Font(Theme.Small, FontStyle.Bold);
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
                ? "Every capture is exactly this many pixels: the box follows the pointer and one click takes it."
                : lockRatio.Checked
                    ? "Dragging is locked to this shape; the size is still up to you."
                    : "Nothing is locked: drag any rectangle you like.";
        }

        private void Lock_CheckedChanged(object sender, EventArgs e)
        {
            SetLockControls();
        }

        private void Cap_Letter_KeyDown(object sender, KeyEventArgs e)
        {
            CaptureKey(Cap_Letter, e);
        }

        /// <summary>Shows the pressed key in the box, ignoring bare modifiers.</summary>
        private static void CaptureKey(TextBox box, KeyEventArgs e)
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
                    box.Text = string.Empty;
                    break;
                default:
                    box.Text = e.KeyCode.ToString();
                    break;
            }
            e.SuppressKeyPress = true;
        }

        private void Browse_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Where should captured images be saved?";
                dialog.SelectedPath = folderBox.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    folderBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void Save_Click(object sender, EventArgs e)
        {
            var letter = Cap_Letter.Text;
            if (letter.Length == 1) letter = letter.ToUpperInvariant();
            Properties.Settings.Default.CaptureHotKey = letter;

            int mods = 0;
            foreach (var mod in _modifiers)
            {
                if (mod.Checked) mods |= int.Parse(mod.Tag.ToString());
            }
            Properties.Settings.Default.CaptureHotKeyModifier = mods;

            Properties.Settings.Default.LockRatio = lockRatio.Checked;
            Properties.Settings.Default.RatioPreset = ratioBox.SelectedItem != null
                ? ratioBox.SelectedItem.ToString()
                : "16:9";
            Properties.Settings.Default.LockPixel = lockPixel.Checked;

            int w, h;
            if (int.TryParse(pixelWidth.Text, out w) && w >= 8) Properties.Settings.Default.PixelWidth = w;
            if (int.TryParse(pixelHeight.Text, out h) && h >= 8) Properties.Settings.Default.PixelHeight = h;

            foreach (var output in _outputs)
            {
                if (output.Checked) Properties.Settings.Default.CaptureOutput = int.Parse(output.Tag.ToString());
            }
            Properties.Settings.Default.CaptureFolder = folderBox.Text.Trim();

            Properties.Settings.Default.Save();
        }
    }
}
