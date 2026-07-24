using System.Drawing;
using System.Windows.Forms;

namespace MicroApp
{
    partial class OcrSettingsForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Designer generated code

        /// <summary>
        /// Same canvas as Key Setting (640 x 612): header band, full-width cards, and
        /// a footer with the primary action.
        /// </summary>
        private void InitializeComponent()
        {
            this.headerBar = new HeaderBar();
            this.iconBox = new PictureBox();
            this.titleLabel = new Label();
            this.subtitleLabel = new Label();

            this.cardHotKey = new Card();
            this.hotKeyHost = new FieldHost();
            this.Ocr_Letter = new TextBox();
            this.keyLabel = new Label();
            this.modifiersLabel = new Label();
            this.Ocr_Alt = new ModernCheckBox();
            this.Ocr_Control = new ModernCheckBox();
            this.Ocr_Shift = new ModernCheckBox();
            this.Ocr_Windows = new ModernCheckBox();

            this.cardLanguage = new Card();
            this.languageHost = new FieldHost();
            this.languageBox = new ComboBox();
            this.engineLabel = new Label();

            this.cardOutput = new Card();
            this.outputClipboard = new ModernRadioButton();
            this.outputPreview = new ModernRadioButton();
            this.outputType = new ModernRadioButton();
            this.keepLines = new ModernCheckBox();

            this.cancelButton = new ModernButton();
            this.saveButton = new ModernButton();

            ((System.ComponentModel.ISupportInitialize)(this.iconBox)).BeginInit();
            this.headerBar.SuspendLayout();
            this.cardHotKey.SuspendLayout();
            this.cardLanguage.SuspendLayout();
            this.cardOutput.SuspendLayout();
            this.SuspendLayout();

            //
            // headerBar
            //
            this.headerBar.Controls.Add(this.iconBox);
            this.headerBar.Controls.Add(this.titleLabel);
            this.headerBar.Controls.Add(this.subtitleLabel);
            this.headerBar.Dock = DockStyle.Top;
            this.headerBar.Name = "headerBar";
            this.headerBar.Size = new Size(640, 84);
            this.headerBar.TabStop = false;
            //
            // iconBox
            //
            this.iconBox.BackColor = Color.Transparent;
            this.iconBox.Location = new Point(24, 22);
            this.iconBox.Name = "iconBox";
            this.iconBox.Size = new Size(40, 40);
            this.iconBox.SizeMode = PictureBoxSizeMode.Zoom;
            this.iconBox.TabStop = false;
            //
            // titleLabel
            //
            this.titleLabel.AutoSize = true;
            this.titleLabel.BackColor = Color.Transparent;
            this.titleLabel.Location = new Point(76, 19);
            this.titleLabel.Name = "titleLabel";
            this.titleLabel.Text = "OCR Setting";
            //
            // subtitleLabel
            //
            this.subtitleLabel.AutoSize = true;
            this.subtitleLabel.BackColor = Color.Transparent;
            this.subtitleLabel.Location = new Point(78, 47);
            this.subtitleLabel.Name = "subtitleLabel";
            this.subtitleLabel.Text = "Drag anywhere on screen to read the text under it";
            //
            // cardHotKey
            //
            this.cardHotKey.Controls.Add(this.hotKeyHost);
            this.cardHotKey.Controls.Add(this.keyLabel);
            this.cardHotKey.Controls.Add(this.modifiersLabel);
            this.cardHotKey.Controls.Add(this.Ocr_Alt);
            this.cardHotKey.Controls.Add(this.Ocr_Control);
            this.cardHotKey.Controls.Add(this.Ocr_Shift);
            this.cardHotKey.Controls.Add(this.Ocr_Windows);
            this.cardHotKey.Location = new Point(24, 104);
            this.cardHotKey.Name = "cardHotKey";
            this.cardHotKey.Size = new Size(592, 124);
            this.cardHotKey.TabIndex = 1;
            this.cardHotKey.Title = "Capture hot key";
            this.cardHotKey.Description = "Press it anywhere to get the crosshair";
            //
            // hotKeyHost
            //
            this.hotKeyHost.Controls.Add(this.Ocr_Letter);
            this.hotKeyHost.Location = new Point(16, 60);
            this.hotKeyHost.Name = "hotKeyHost";
            this.hotKeyHost.Size = new Size(96, 32);
            this.hotKeyHost.TabIndex = 0;
            //
            // Ocr_Letter
            //
            this.Ocr_Letter.Location = new Point(10, 8);
            this.Ocr_Letter.Name = "Ocr_Letter";
            this.Ocr_Letter.Size = new Size(76, 16);
            this.Ocr_Letter.TabIndex = 0;
            this.Ocr_Letter.KeyDown += new KeyEventHandler(this.Ocr_Letter_KeyDown);
            //
            // keyLabel
            //
            this.keyLabel.AutoSize = true;
            this.keyLabel.Location = new Point(120, 69);
            this.keyLabel.Name = "keyLabel";
            this.keyLabel.Text = "key";
            //
            // modifiersLabel
            //
            this.modifiersLabel.AutoSize = true;
            this.modifiersLabel.Location = new Point(200, 44);
            this.modifiersLabel.Name = "modifiersLabel";
            this.modifiersLabel.Text = "HELD WITH";
            //
            // Ocr_Alt
            //
            this.Ocr_Alt.Location = new Point(200, 62);
            this.Ocr_Alt.Name = "Ocr_Alt";
            this.Ocr_Alt.Size = new Size(88, 24);
            this.Ocr_Alt.TabIndex = 1;
            this.Ocr_Alt.Tag = "1";
            this.Ocr_Alt.Text = "Alt";
            //
            // Ocr_Control
            //
            this.Ocr_Control.Location = new Point(296, 62);
            this.Ocr_Control.Name = "Ocr_Control";
            this.Ocr_Control.Size = new Size(88, 24);
            this.Ocr_Control.TabIndex = 2;
            this.Ocr_Control.Tag = "2";
            this.Ocr_Control.Text = "Ctrl";
            //
            // Ocr_Shift
            //
            this.Ocr_Shift.Location = new Point(392, 62);
            this.Ocr_Shift.Name = "Ocr_Shift";
            this.Ocr_Shift.Size = new Size(88, 24);
            this.Ocr_Shift.TabIndex = 3;
            this.Ocr_Shift.Tag = "4";
            this.Ocr_Shift.Text = "Shift";
            //
            // Ocr_Windows
            //
            this.Ocr_Windows.Location = new Point(488, 62);
            this.Ocr_Windows.Name = "Ocr_Windows";
            this.Ocr_Windows.Size = new Size(88, 24);
            this.Ocr_Windows.TabIndex = 4;
            this.Ocr_Windows.Tag = "8";
            this.Ocr_Windows.Text = "Win";
            //
            // cardLanguage
            //
            this.cardLanguage.Controls.Add(this.languageHost);
            this.cardLanguage.Controls.Add(this.engineLabel);
            this.cardLanguage.Location = new Point(24, 244);
            this.cardLanguage.Name = "cardLanguage";
            this.cardLanguage.Size = new Size(592, 120);
            this.cardLanguage.TabIndex = 2;
            this.cardLanguage.Title = "Language";
            this.cardLanguage.Description = "Which recognizer Windows should use";
            //
            // languageHost
            //
            this.languageHost.Controls.Add(this.languageBox);
            this.languageHost.Location = new Point(16, 58);
            this.languageHost.Name = "languageHost";
            this.languageHost.Size = new Size(300, 32);
            this.languageHost.TabIndex = 0;
            //
            // languageBox
            //
            this.languageBox.Location = new Point(10, 6);
            this.languageBox.Name = "languageBox";
            this.languageBox.Size = new Size(280, 21);
            this.languageBox.TabIndex = 0;
            //
            // engineLabel
            //
            this.engineLabel.Location = new Point(16, 94);
            this.engineLabel.Name = "engineLabel";
            this.engineLabel.Size = new Size(560, 20);
            this.engineLabel.Text = "Windows OCR";
            //
            // cardOutput
            //
            this.cardOutput.Controls.Add(this.outputClipboard);
            this.cardOutput.Controls.Add(this.outputPreview);
            this.cardOutput.Controls.Add(this.outputType);
            this.cardOutput.Controls.Add(this.keepLines);
            this.cardOutput.Location = new Point(24, 380);
            this.cardOutput.Name = "cardOutput";
            this.cardOutput.Size = new Size(592, 172);
            this.cardOutput.TabIndex = 3;
            this.cardOutput.Title = "After capture";
            this.cardOutput.Description = "What to do with the text that comes back";
            //
            // outputClipboard
            //
            this.outputClipboard.Location = new Point(16, 56);
            this.outputClipboard.Name = "outputClipboard";
            this.outputClipboard.Size = new Size(560, 24);
            this.outputClipboard.TabIndex = 0;
            this.outputClipboard.TabStop = true;
            this.outputClipboard.Tag = "0";
            this.outputClipboard.Text = "Copy it to the clipboard";
            //
            // outputPreview
            //
            this.outputPreview.Location = new Point(16, 84);
            this.outputPreview.Name = "outputPreview";
            this.outputPreview.Size = new Size(560, 24);
            this.outputPreview.TabIndex = 1;
            this.outputPreview.TabStop = true;
            this.outputPreview.Tag = "1";
            this.outputPreview.Text = "Show it in a window first";
            //
            // outputType
            //
            this.outputType.Location = new Point(16, 112);
            this.outputType.Name = "outputType";
            this.outputType.Size = new Size(560, 24);
            this.outputType.TabIndex = 2;
            this.outputType.TabStop = true;
            this.outputType.Tag = "2";
            this.outputType.Text = "Type it straight into the window I was using";
            //
            // keepLines
            //
            this.keepLines.Location = new Point(16, 144);
            this.keepLines.Name = "keepLines";
            this.keepLines.Size = new Size(560, 24);
            this.keepLines.TabIndex = 3;
            this.keepLines.Text = "Keep the original line breaks";
            //
            // cancelButton
            //
            this.cancelButton.DialogResult = DialogResult.Cancel;
            this.cancelButton.Location = new Point(436, 564);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new Size(88, 36);
            this.cancelButton.TabIndex = 4;
            this.cancelButton.Text = "Cancel";
            //
            // saveButton
            //
            this.saveButton.Accent = true;
            this.saveButton.DialogResult = DialogResult.OK;
            this.saveButton.Location = new Point(536, 564);
            this.saveButton.Name = "saveButton";
            this.saveButton.Size = new Size(80, 36);
            this.saveButton.TabIndex = 5;
            this.saveButton.Text = "Save";
            this.saveButton.Click += new System.EventHandler(this.Save_Click);
            //
            // OcrSettingsForm
            //
            this.AcceptButton = this.saveButton;
            this.CancelButton = this.cancelButton;
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(640, 612);
            this.Controls.Add(this.cardHotKey);
            this.Controls.Add(this.cardLanguage);
            this.Controls.Add(this.cardOutput);
            this.Controls.Add(this.cancelButton);
            this.Controls.Add(this.saveButton);
            this.Controls.Add(this.headerBar);
            this.Font = new Font("Segoe UI", 9F);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "OcrSettingsForm";
            this.SizeGripStyle = SizeGripStyle.Hide;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "MicroApp OCR Setting";
            ((System.ComponentModel.ISupportInitialize)(this.iconBox)).EndInit();
            this.headerBar.ResumeLayout(false);
            this.headerBar.PerformLayout();
            this.cardHotKey.ResumeLayout(false);
            this.cardHotKey.PerformLayout();
            this.cardLanguage.ResumeLayout(false);
            this.cardOutput.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private HeaderBar headerBar;
        private PictureBox iconBox;
        private Label titleLabel;
        private Label subtitleLabel;

        private Card cardHotKey;
        private FieldHost hotKeyHost;
        private TextBox Ocr_Letter;
        private Label keyLabel;
        private Label modifiersLabel;
        private ModernCheckBox Ocr_Alt;
        private ModernCheckBox Ocr_Control;
        private ModernCheckBox Ocr_Shift;
        private ModernCheckBox Ocr_Windows;

        private Card cardLanguage;
        private FieldHost languageHost;
        private ComboBox languageBox;
        private Label engineLabel;

        private Card cardOutput;
        private ModernRadioButton outputClipboard;
        private ModernRadioButton outputPreview;
        private ModernRadioButton outputType;
        private ModernCheckBox keepLines;

        private ModernButton cancelButton;
        private ModernButton saveButton;
    }
}
