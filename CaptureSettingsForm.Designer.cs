using System.Drawing;
using System.Windows.Forms;

namespace MicroApp
{
    partial class CaptureSettingsForm
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

        /// <summary>Same canvas as Key Setting (640 x 612).</summary>
        private void InitializeComponent()
        {
            this.headerBar = new HeaderBar();
            this.iconBox = new PictureBox();
            this.titleLabel = new Label();
            this.subtitleLabel = new Label();

            this.cardHotKey = new Card();
            this.hotKeyHost = new FieldHost();
            this.Cap_Letter = new TextBox();
            this.keyLabel = new Label();
            this.modifiersLabel = new Label();
            this.Cap_Alt = new ModernCheckBox();
            this.Cap_Control = new ModernCheckBox();
            this.Cap_Shift = new ModernCheckBox();
            this.Cap_Windows = new ModernCheckBox();

            this.cardLock = new Card();
            this.lockRatio = new ModernCheckBox();
            this.ratioHost = new FieldHost();
            this.ratioBox = new ComboBox();
            this.lockPixel = new ModernCheckBox();
            this.pixelWidthHost = new FieldHost();
            this.pixelWidth = new TextBox();
            this.byLabel = new Label();
            this.pixelHeightHost = new FieldHost();
            this.pixelHeight = new TextBox();
            this.pxLabel = new Label();
            this.lockNote = new Label();

            this.cardOutput = new Card();
            this.outputClipboard = new ModernRadioButton();
            this.outputFile = new ModernRadioButton();
            this.outputBoth = new ModernRadioButton();
            this.folderLabel = new Label();
            this.folderHost = new FieldHost();
            this.folderBox = new TextBox();
            this.browseButton = new ModernButton();

            this.cancelButton = new ModernButton();
            this.saveButton = new ModernButton();

            ((System.ComponentModel.ISupportInitialize)(this.iconBox)).BeginInit();
            this.headerBar.SuspendLayout();
            this.cardHotKey.SuspendLayout();
            this.cardLock.SuspendLayout();
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
            this.titleLabel.Text = "Capture Setting";
            //
            // subtitleLabel
            //
            this.subtitleLabel.AutoSize = true;
            this.subtitleLabel.BackColor = Color.Transparent;
            this.subtitleLabel.Location = new Point(78, 47);
            this.subtitleLabel.Name = "subtitleLabel";
            this.subtitleLabel.Text = "Screen capture only. GIF recording has its own settings.";
            //
            // cardHotKey
            //
            this.cardHotKey.Controls.Add(this.hotKeyHost);
            this.cardHotKey.Controls.Add(this.keyLabel);
            this.cardHotKey.Controls.Add(this.modifiersLabel);
            this.cardHotKey.Controls.Add(this.Cap_Alt);
            this.cardHotKey.Controls.Add(this.Cap_Control);
            this.cardHotKey.Controls.Add(this.Cap_Shift);
            this.cardHotKey.Controls.Add(this.Cap_Windows);
            this.cardHotKey.Location = new Point(24, 104);
            this.cardHotKey.Name = "cardHotKey";
            this.cardHotKey.Size = new Size(592, 124);
            this.cardHotKey.TabIndex = 1;
            this.cardHotKey.Title = "Screen capture hot key";
            this.cardHotKey.Description = "Press it anywhere to grab a picture of the screen";
            //
            // hotKeyHost
            //
            this.hotKeyHost.Controls.Add(this.Cap_Letter);
            this.hotKeyHost.Location = new Point(16, 60);
            this.hotKeyHost.Name = "hotKeyHost";
            this.hotKeyHost.Size = new Size(96, 32);
            this.hotKeyHost.TabIndex = 0;
            //
            // Cap_Letter
            //
            this.Cap_Letter.Location = new Point(10, 8);
            this.Cap_Letter.Name = "Cap_Letter";
            this.Cap_Letter.Size = new Size(76, 16);
            this.Cap_Letter.TabIndex = 0;
            this.Cap_Letter.KeyDown += new KeyEventHandler(this.Cap_Letter_KeyDown);
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
            // Cap_Alt
            //
            this.Cap_Alt.Location = new Point(200, 62);
            this.Cap_Alt.Name = "Cap_Alt";
            this.Cap_Alt.Size = new Size(88, 24);
            this.Cap_Alt.TabIndex = 1;
            this.Cap_Alt.Tag = "1";
            this.Cap_Alt.Text = "Alt";
            //
            // Cap_Control
            //
            this.Cap_Control.Location = new Point(296, 62);
            this.Cap_Control.Name = "Cap_Control";
            this.Cap_Control.Size = new Size(88, 24);
            this.Cap_Control.TabIndex = 2;
            this.Cap_Control.Tag = "2";
            this.Cap_Control.Text = "Ctrl";
            //
            // Cap_Shift
            //
            this.Cap_Shift.Location = new Point(392, 62);
            this.Cap_Shift.Name = "Cap_Shift";
            this.Cap_Shift.Size = new Size(88, 24);
            this.Cap_Shift.TabIndex = 3;
            this.Cap_Shift.Tag = "4";
            this.Cap_Shift.Text = "Shift";
            //
            // Cap_Windows
            //
            this.Cap_Windows.Location = new Point(488, 62);
            this.Cap_Windows.Name = "Cap_Windows";
            this.Cap_Windows.Size = new Size(88, 24);
            this.Cap_Windows.TabIndex = 4;
            this.Cap_Windows.Tag = "8";
            this.Cap_Windows.Text = "Win";
            //
            // cardLock
            //
            this.cardLock.Controls.Add(this.lockRatio);
            this.cardLock.Controls.Add(this.ratioHost);
            this.cardLock.Controls.Add(this.lockPixel);
            this.cardLock.Controls.Add(this.pixelWidthHost);
            this.cardLock.Controls.Add(this.byLabel);
            this.cardLock.Controls.Add(this.pixelHeightHost);
            this.cardLock.Controls.Add(this.pxLabel);
            this.cardLock.Controls.Add(this.lockNote);
            this.cardLock.Location = new Point(24, 244);
            this.cardLock.Name = "cardLock";
            this.cardLock.Size = new Size(592, 152);
            this.cardLock.TabIndex = 2;
            this.cardLock.Title = "Selection lock";
            this.cardLock.Description = "Constrain the shape of the capture box";
            //
            // lockRatio
            //
            this.lockRatio.Location = new Point(16, 56);
            this.lockRatio.Name = "lockRatio";
            this.lockRatio.Size = new Size(150, 24);
            this.lockRatio.TabIndex = 0;
            this.lockRatio.Text = "Lock ratio";
            this.lockRatio.CheckedChanged += new System.EventHandler(this.Lock_CheckedChanged);
            //
            // ratioHost
            //
            this.ratioHost.Controls.Add(this.ratioBox);
            this.ratioHost.Location = new Point(176, 54);
            this.ratioHost.Name = "ratioHost";
            this.ratioHost.Size = new Size(130, 32);
            this.ratioHost.TabIndex = 1;
            //
            // ratioBox
            //
            this.ratioBox.Location = new Point(10, 6);
            this.ratioBox.Name = "ratioBox";
            this.ratioBox.Size = new Size(110, 21);
            this.ratioBox.TabIndex = 0;
            //
            // lockPixel
            //
            this.lockPixel.Location = new Point(16, 92);
            this.lockPixel.Name = "lockPixel";
            this.lockPixel.Size = new Size(150, 24);
            this.lockPixel.TabIndex = 2;
            this.lockPixel.Text = "Lock pixel size";
            this.lockPixel.CheckedChanged += new System.EventHandler(this.Lock_CheckedChanged);
            //
            // pixelWidthHost
            //
            this.pixelWidthHost.Controls.Add(this.pixelWidth);
            this.pixelWidthHost.Location = new Point(176, 90);
            this.pixelWidthHost.Name = "pixelWidthHost";
            this.pixelWidthHost.Size = new Size(84, 32);
            this.pixelWidthHost.TabIndex = 3;
            //
            // pixelWidth
            //
            this.pixelWidth.Location = new Point(10, 8);
            this.pixelWidth.Name = "pixelWidth";
            this.pixelWidth.Size = new Size(64, 16);
            this.pixelWidth.TabIndex = 0;
            //
            // byLabel
            //
            this.byLabel.AutoSize = true;
            this.byLabel.Location = new Point(268, 99);
            this.byLabel.Name = "byLabel";
            this.byLabel.Text = "x";
            //
            // pixelHeightHost
            //
            this.pixelHeightHost.Controls.Add(this.pixelHeight);
            this.pixelHeightHost.Location = new Point(286, 90);
            this.pixelHeightHost.Name = "pixelHeightHost";
            this.pixelHeightHost.Size = new Size(84, 32);
            this.pixelHeightHost.TabIndex = 4;
            //
            // pixelHeight
            //
            this.pixelHeight.Location = new Point(10, 8);
            this.pixelHeight.Name = "pixelHeight";
            this.pixelHeight.Size = new Size(64, 16);
            this.pixelHeight.TabIndex = 0;
            //
            // pxLabel
            //
            this.pxLabel.AutoSize = true;
            this.pxLabel.Location = new Point(378, 99);
            this.pxLabel.Name = "pxLabel";
            this.pxLabel.Text = "px";
            //
            // lockNote
            //
            this.lockNote.Location = new Point(16, 124);
            this.lockNote.Name = "lockNote";
            this.lockNote.Size = new Size(560, 20);
            this.lockNote.Text = "Nothing is locked.";
            //
            // cardOutput
            //
            this.cardOutput.Controls.Add(this.outputClipboard);
            this.cardOutput.Controls.Add(this.outputFile);
            this.cardOutput.Controls.Add(this.outputBoth);
            this.cardOutput.Controls.Add(this.folderLabel);
            this.cardOutput.Controls.Add(this.folderHost);
            this.cardOutput.Controls.Add(this.browseButton);
            this.cardOutput.Location = new Point(24, 412);
            this.cardOutput.Name = "cardOutput";
            this.cardOutput.Size = new Size(592, 140);
            this.cardOutput.TabIndex = 3;
            this.cardOutput.Title = "After capture";
            this.cardOutput.Description = "What to do with the picture";
            //
            // outputClipboard
            //
            this.outputClipboard.Location = new Point(16, 52);
            this.outputClipboard.Name = "outputClipboard";
            this.outputClipboard.Size = new Size(190, 24);
            this.outputClipboard.TabIndex = 0;
            this.outputClipboard.TabStop = true;
            this.outputClipboard.Tag = "0";
            this.outputClipboard.Text = "Copy to clipboard";
            //
            // outputFile
            //
            this.outputFile.Location = new Point(216, 52);
            this.outputFile.Name = "outputFile";
            this.outputFile.Size = new Size(150, 24);
            this.outputFile.TabIndex = 1;
            this.outputFile.TabStop = true;
            this.outputFile.Tag = "1";
            this.outputFile.Text = "Save as PNG";
            //
            // outputBoth
            //
            this.outputBoth.Location = new Point(376, 52);
            this.outputBoth.Name = "outputBoth";
            this.outputBoth.Size = new Size(200, 24);
            this.outputBoth.TabIndex = 2;
            this.outputBoth.TabStop = true;
            this.outputBoth.Tag = "2";
            this.outputBoth.Text = "Copy and save";
            //
            // folderLabel
            //
            this.folderLabel.AutoSize = true;
            this.folderLabel.Location = new Point(16, 82);
            this.folderLabel.Name = "folderLabel";
            this.folderLabel.Text = "IMAGE FOLDER";
            //
            // folderHost
            //
            this.folderHost.Controls.Add(this.folderBox);
            this.folderHost.Location = new Point(16, 100);
            this.folderHost.Name = "folderHost";
            this.folderHost.Size = new Size(470, 32);
            this.folderHost.TabIndex = 3;
            //
            // folderBox
            //
            this.folderBox.Location = new Point(10, 8);
            this.folderBox.Name = "folderBox";
            this.folderBox.Size = new Size(450, 16);
            this.folderBox.TabIndex = 0;
            //
            // browseButton
            //
            this.browseButton.Location = new Point(500, 100);
            this.browseButton.Name = "browseButton";
            this.browseButton.Size = new Size(76, 32);
            this.browseButton.TabIndex = 4;
            this.browseButton.Text = "Browse";
            this.browseButton.Click += new System.EventHandler(this.Browse_Click);
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
            // CaptureSettingsForm
            //
            this.AcceptButton = this.saveButton;
            this.CancelButton = this.cancelButton;
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(640, 612);
            this.Controls.Add(this.cardHotKey);
            this.Controls.Add(this.cardLock);
            this.Controls.Add(this.cardOutput);
            this.Controls.Add(this.cancelButton);
            this.Controls.Add(this.saveButton);
            this.Controls.Add(this.headerBar);
            this.Font = new Font("Segoe UI", 9F);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "CaptureSettingsForm";
            this.SizeGripStyle = SizeGripStyle.Hide;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "MicroApp Capture Setting";
            ((System.ComponentModel.ISupportInitialize)(this.iconBox)).EndInit();
            this.headerBar.ResumeLayout(false);
            this.headerBar.PerformLayout();
            this.cardHotKey.ResumeLayout(false);
            this.cardHotKey.PerformLayout();
            this.cardLock.ResumeLayout(false);
            this.cardLock.PerformLayout();
            this.cardOutput.ResumeLayout(false);
            this.cardOutput.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion

        private HeaderBar headerBar;
        private PictureBox iconBox;
        private Label titleLabel;
        private Label subtitleLabel;

        private Card cardHotKey;
        private FieldHost hotKeyHost;
        private TextBox Cap_Letter;
        private Label keyLabel;
        private Label modifiersLabel;
        private ModernCheckBox Cap_Alt;
        private ModernCheckBox Cap_Control;
        private ModernCheckBox Cap_Shift;
        private ModernCheckBox Cap_Windows;

        private Card cardLock;
        private ModernCheckBox lockRatio;
        private FieldHost ratioHost;
        private ComboBox ratioBox;
        private ModernCheckBox lockPixel;
        private FieldHost pixelWidthHost;
        private TextBox pixelWidth;
        private Label byLabel;
        private FieldHost pixelHeightHost;
        private TextBox pixelHeight;
        private Label pxLabel;
        private Label lockNote;

        private Card cardOutput;
        private ModernRadioButton outputClipboard;
        private ModernRadioButton outputFile;
        private ModernRadioButton outputBoth;
        private Label folderLabel;
        private FieldHost folderHost;
        private TextBox folderBox;
        private ModernButton browseButton;

        private ModernButton cancelButton;
        private ModernButton saveButton;
    }
}
