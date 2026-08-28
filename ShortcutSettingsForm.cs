using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// Every hot key in one place. The rows edit the SAME settings the feature windows do
    /// (Key/OCR/Capture/GIF/Video/Note Setting), so a change made here shows up there and a
    /// change made there shows up here - one value, two doors. Also home to the two typed-date
    /// hot keys and their formats (shared with the note toolbar's date buttons).
    /// </summary>
    class ShortcutSettingsForm : PixelPerfectForm
    {
        class HotKeyRow
        {
            public string Label;
            public string KeySetting;
            public string ModSetting;
            public TextBox Letter;
            public ModernCheckBox[] Mods;   // Alt=1, Ctrl=2, Shift=4, Win=8
        }

        readonly List<HotKeyRow> _rows = new List<HotKeyRow>();
        TextBox _dateBox, _longDateBox;
        Label _datePreview, _longDatePreview;

        static readonly string[] ModNames = { "Alt", "Ctrl", "Shift", "Win" };
        static readonly int[] ModBits = { 1, 2, 4, 8 };
        const int RowH = 31;

        public ShortcutSettingsForm()
        {
            Theme.Init(ThemeHelper.IsDarkMode);

            Text = "MicroApp - Shortcuts";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            Font = Theme.Base;

            SuspendLayout();

            var iconBox = new PictureBox
            {
                Location = new Point(28, 24),
                Size = new Size(40, 40),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                TabStop = false
            };
            using (var large = new Icon(Properties.Resources.AppIcon, 40, 40))
                iconBox.Image = large.ToBitmap();

            var heading = new Label
            {
                Location = new Point(84, 24),
                AutoSize = true,
                Text = "Shortcuts",
                Font = new Font("Segoe UI Semibold", 12.5F),
                ForeColor = Theme.Text,
                BackColor = Color.Transparent
            };
            var subtitle = new Label
            {
                Location = new Point(86, 52),
                Size = new Size(470, 32),
                Text = "Every hot key in one place. These are the same values the feature windows show - " +
                       "change one anywhere and it changes everywhere. Clear the key box to turn one off.",
                Font = Theme.Small,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent
            };
            Controls.Add(iconBox);
            Controls.Add(heading);
            Controls.Add(subtitle);

            // ----- hot keys card ------------------------------------------------------
            var card = new Card
            {
                Location = new Point(24, 96),
                Size = new Size(548, 92 + (RowH * 10)),
                Title = "Hot keys",
                Description = "Tick the modifiers and give each action a key."
            };
            Controls.Add(card);

            int headY = 58;
            int colLabel = 20, colMods = 208, colKey = colMods + 4 * 62 + 6;
            for (int m = 0; m < 4; m++)
            {
                card.Controls.Add(new Label
                {
                    Location = new Point(colMods + m * 62, headY),
                    AutoSize = true,
                    Text = ModNames[m],
                    Font = Theme.Small,
                    ForeColor = Theme.TextDim,
                    BackColor = Color.Transparent
                });
            }
            card.Controls.Add(new Label
            {
                Location = new Point(colKey + 2, headY),
                AutoSize = true,
                Text = "Key",
                Font = Theme.Small,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent
            });

            int y = headY + 24;
            AddRow(card, y + RowH * 0, colLabel, colMods, colKey, "Paste as keystrokes", "HotKey", "HotKeyModifier");
            AddRow(card, y + RowH * 1, colLabel, colMods, colKey, "Grab text (OCR)", "OcrHotKey", "OcrHotKeyModifier");
            AddRow(card, y + RowH * 2, colLabel, colMods, colKey, "Pick Text", "TextPickHotKey", "TextPickHotKeyModifier");
            AddRow(card, y + RowH * 3, colLabel, colMods, colKey, "Screen capture", "CaptureHotKey", "CaptureHotKeyModifier");
            AddRow(card, y + RowH * 4, colLabel, colMods, colKey, "Record GIF", "GifHotKey", "GifHotKeyModifier");
            AddRow(card, y + RowH * 5, colLabel, colMods, colKey, "Record Video", "VideoHotKey", "VideoHotKeyModifier");
            AddRow(card, y + RowH * 6, colLabel, colMods, colKey, "New note", "NoteHotKey", "NoteHotKeyModifier");
            AddRow(card, y + RowH * 7, colLabel, colMods, colKey, "Image editor", "ImageEditorHotKey", "ImageEditorHotKeyModifier");
            AddRow(card, y + RowH * 8, colLabel, colMods, colKey, "Type the date", "DateHotKey", "DateHotKeyModifier");
            AddRow(card, y + RowH * 9, colLabel, colMods, colKey, "Type the long date", "LongDateHotKey", "LongDateHotKeyModifier");

            // ----- date formats card --------------------------------------------------
            var dates = new Card
            {
                Location = new Point(24, card.Bottom + 12),
                Size = new Size(548, 150),
                Title = "Typed dates",
                Description = "What the date hot keys type into the focused window. Shared with the note toolbar's date buttons."
            };
            Controls.Add(dates);

            _dateBox = FormatRow(dates, 62, "Date", Properties.Settings.Default.NoteDateFormat, out _datePreview);
            _longDateBox = FormatRow(dates, 94, "Long date", Properties.Settings.Default.NoteLongDateFormat, out _longDatePreview);
            UpdatePreviews();

            // ----- buttons ------------------------------------------------------------
            var save = new ModernButton
            {
                Text = "Save",
                Accent = true,
                Size = new Size(112, 36),
                Location = new Point(548 + 24 - 112, dates.Bottom + 14)
            };
            save.Click += Save_Click;
            var cancel = new ModernButton
            {
                Text = "Cancel",
                Size = new Size(96, 36),
                Location = new Point(save.Left - 96 - 8, dates.Bottom + 14),
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(save);
            Controls.Add(cancel);
            AcceptButton = save;
            CancelButton = cancel;

            ClientSize = new Size(548 + 48, save.Bottom + 20);
            ResumeLayout();

            Theme.Apply(this);
            Native.SetDarkModeForWindow(Handle, ThemeHelper.IsDarkMode);
            Theme.RoundWindowCorners(Handle);
            Icon = Properties.Resources.AppIcon;
        }

        void AddRow(Card card, int y, int colLabel, int colMods, int colKey, string label, string keySetting, string modSetting)
        {
            var row = new HotKeyRow { Label = label, KeySetting = keySetting, ModSetting = modSetting };

            card.Controls.Add(new Label
            {
                Location = new Point(colLabel, y + 4),
                AutoSize = true,
                Text = label,
                Font = Theme.Base,
                ForeColor = Theme.Text,
                BackColor = Color.Transparent
            });

            int mods = (int)Properties.Settings.Default[modSetting];
            row.Mods = new ModernCheckBox[4];
            for (int m = 0; m < 4; m++)
            {
                var check = new ModernCheckBox
                {
                    Location = new Point(colMods + m * 62 - 2, y + 2),
                    AutoSize = true,
                    Text = "",
                    Checked = (mods & ModBits[m]) != 0
                };
                row.Mods[m] = check;
                card.Controls.Add(check);
            }

            row.Letter = new TextBox
            {
                Location = new Point(colKey, y + 1),
                Size = new Size(42, 24),
                MaxLength = 12,
                Text = (string)Properties.Settings.Default[keySetting],
                TextAlign = HorizontalAlignment.Center,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.FieldBg,
                ForeColor = Theme.Text,
                CharacterCasing = CharacterCasing.Upper
            };
            card.Controls.Add(row.Letter);
            _rows.Add(row);
        }

        TextBox FormatRow(Card card, int y, string label, string value, out Label preview)
        {
            card.Controls.Add(new Label
            {
                Location = new Point(20, y + 4),
                AutoSize = true,
                Text = label,
                Font = Theme.Base,
                ForeColor = Theme.Text,
                BackColor = Color.Transparent
            });
            var box = new TextBox
            {
                Location = new Point(120, y + 1),
                Size = new Size(180, 24),
                Text = value,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.FieldBg,
                ForeColor = Theme.Text
            };
            var pv = new Label
            {
                Location = new Point(312, y + 4),
                Size = new Size(220, 20),
                Font = Theme.Small,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent
            };
            box.TextChanged += delegate { UpdatePreviews(); };
            card.Controls.Add(box);
            card.Controls.Add(pv);
            preview = pv;
            return box;
        }

        void UpdatePreviews()
        {
            _datePreview.Text = Preview(_dateBox.Text, "yyyy-MM-dd");
            _longDatePreview.Text = Preview(_longDateBox.Text, "dddd, dd MMMM yyyy");
        }

        static string Preview(string format, string fallback)
        {
            if (string.IsNullOrWhiteSpace(format)) format = fallback;
            try { return DateTime.Now.ToString(format); }
            catch (FormatException) { return "invalid format"; }
        }

        void Save_Click(object sender, EventArgs e)
        {
            // two actions on one combination would fight over the registration - catch it here
            var seen = new Dictionary<string, string>();
            foreach (HotKeyRow row in _rows)
            {
                string letter = row.Letter.Text.Trim();
                if (letter.Length == 0) continue;
                int mods = RowMods(row);
                string combo = mods + "+" + letter.ToUpperInvariant();
                string other;
                if (seen.TryGetValue(combo, out other))
                {
                    ModernDialog.Info("Two actions share one hot key",
                        "“" + other + "” and “" + row.Label + "” are both set to the same combination.\r\n\r\n" +
                        "Give one of them a different key and save again.");
                    return;
                }
                seen[combo] = row.Label;
            }

            foreach (HotKeyRow row in _rows)
            {
                string letter = row.Letter.Text.Trim();
                if (letter.Length == 1) letter = letter.ToUpperInvariant();
                Properties.Settings.Default[row.KeySetting] = letter;
                Properties.Settings.Default[row.ModSetting] = RowMods(row);
            }
            Properties.Settings.Default.NoteDateFormat = _dateBox.Text.Trim();
            Properties.Settings.Default.NoteLongDateFormat = _longDateBox.Text.Trim();
            Properties.Settings.Default.Save();
            DialogResult = DialogResult.OK;
            Close();
        }

        static int RowMods(HotKeyRow row)
        {
            int mods = 0;
            for (int m = 0; m < 4; m++)
                if (row.Mods[m].Checked) mods |= ModBits[m];
            return mods;
        }
    }
}
