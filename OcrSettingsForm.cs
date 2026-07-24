using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>Where recognised text goes once a capture finishes.</summary>
    public enum OcrOutput
    {
        Clipboard = 0,
        Preview = 1,
        Type = 2
    }

    public partial class OcrSettingsForm : Form
    {
        private CheckBox[] _modifiers;
        private RadioButton[] _outputs;

        public OcrSettingsForm()
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

            _modifiers = new CheckBox[] { Ocr_Alt, Ocr_Control, Ocr_Shift, Ocr_Windows };
            _outputs = new RadioButton[] { outputClipboard, outputPreview, outputType };

            LoadLanguages();

            Ocr_Letter.Text = Properties.Settings.Default.OcrHotKey;
            foreach (var mod in _modifiers)
            {
                mod.Checked = (0 != (Properties.Settings.Default.OcrHotKeyModifier & int.Parse(mod.Tag.ToString())));
            }
            foreach (var output in _outputs)
            {
                output.Checked = (Properties.Settings.Default.OcrOutput == int.Parse(output.Tag.ToString()));
            }
            keepLines.Checked = Properties.Settings.Default.OcrKeepLines;
        }

        private void StyleText()
        {
            titleLabel.Font = Theme.Heading;
            titleLabel.ForeColor = Theme.Text;
            subtitleLabel.Font = Theme.Small;
            subtitleLabel.ForeColor = Theme.TextDim;

            modifiersLabel.Font = new Font(Theme.Small, FontStyle.Bold);
            modifiersLabel.ForeColor = Theme.TextDim;

            foreach (var helper in new[] { keyLabel, engineLabel })
            {
                helper.Font = Theme.Base;
                helper.ForeColor = Theme.TextDim;
            }
        }

        private void LoadLanguages()
        {
            languageBox.Items.Clear();
            languageBox.Items.Add(new LanguageChoice(string.Empty, "Use my Windows languages"));

            var installed = OcrService.AvailableLanguages();
            foreach (var lang in installed)
            {
                languageBox.Items.Add(new LanguageChoice(lang.Key, lang.Value));
            }

            string saved = Properties.Settings.Default.OcrLanguage ?? string.Empty;
            languageBox.SelectedIndex = 0;
            for (int i = 0; i < languageBox.Items.Count; i++)
            {
                if (((LanguageChoice)languageBox.Items[i]).Tag == saved)
                {
                    languageBox.SelectedIndex = i;
                    break;
                }
            }

            engineLabel.Text = installed.Count == 0
                ? "No OCR language pack is installed. Add one in Windows Settings > Time & language."
                : $"Windows OCR, {installed.Count} language pack" + (installed.Count == 1 ? "" : "s") + " installed.";
        }

        private void Ocr_Letter_KeyDown(object sender, KeyEventArgs e)
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
                    Ocr_Letter.Text = string.Empty;
                    break;
                default:
                    Ocr_Letter.Text = e.KeyCode.ToString();
                    break;
            }
            e.SuppressKeyPress = true;
        }

        private void Save_Click(object sender, EventArgs e)
        {
            var letter = Ocr_Letter.Text;
            if (letter.Length == 1) letter = letter.ToUpperInvariant();
            Properties.Settings.Default.OcrHotKey = letter;

            int mods = 0;
            foreach (var mod in _modifiers)
            {
                if (mod.Checked) mods |= int.Parse(mod.Tag.ToString());
            }
            Properties.Settings.Default.OcrHotKeyModifier = mods;

            foreach (var output in _outputs)
            {
                if (output.Checked) Properties.Settings.Default.OcrOutput = int.Parse(output.Tag.ToString());
            }

            var choice = languageBox.SelectedItem as LanguageChoice;
            Properties.Settings.Default.OcrLanguage = choice != null ? choice.Tag : string.Empty;
            Properties.Settings.Default.OcrKeepLines = keepLines.Checked;
            Properties.Settings.Default.Save();
        }

        /// <summary>Combo entry pairing a BCP-47 tag with its display name.</summary>
        private class LanguageChoice
        {
            public string Tag { get; private set; }
            public string Display { get; private set; }

            public LanguageChoice(string tag, string display)
            {
                Tag = tag;
                Display = display;
            }

            public override string ToString()
            {
                return Display;
            }
        }
    }
}
