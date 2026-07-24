using System.Drawing;
using System.Windows.Forms;

namespace MicroApp
{
    partial class GifSettingsForm
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

            this.cardRecording = new Card();
            this.hotKeyHost = new FieldHost();
            this.Gif_Letter = new TextBox();
            this.keyLabel = new Label();
            this.modifiersLabel = new Label();
            this.Gif_Alt = new ModernCheckBox();
            this.Gif_Control = new ModernCheckBox();
            this.Gif_Shift = new ModernCheckBox();
            this.Gif_Windows = new ModernCheckBox();
            this.fpsHost = new FieldHost();
            this.gifFps = new TextBox();
            this.fpsLabel = new Label();
            this.secondsHost = new FieldHost();
            this.gifSeconds = new TextBox();
            this.secondsLabel = new Label();

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
            this.outputSave = new ModernRadioButton();
            this.outputCopyPath = new ModernRadioButton();
            this.outputOpen = new ModernRadioButton();
            this.folderHost = new FieldHost();
            this.folderBox = new TextBox();
            this.browseButton = new ModernButton();

            this.cancelButton = new ModernButton();
            this.saveButton = new ModernButton();

            ((System.ComponentModel.ISupportInitialize)(this.iconBox)).BeginInit();
            this.headerBar.SuspendLayout();
            this.cardRecording.SuspendLayout();
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
            this.titleLabel.Text = "GIF Setting";
            //
            // subtitleLabel
            //
            this.subtitleLabel.AutoSize = true;
            this.subtitleLabel.BackColor = Color.Transparent;
            this.subtitleLabel.Location = new Point(78, 47);
            this.subtitleLabel.Name = "subtitleLabel";
            this.subtitleLabel.Text = "Recording, and the lock and output used only for GIFs";
            //
            // cardRecording
            //
            this.cardRecording.Controls.Add(this.hotKeyHost);
            this.cardRecording.Controls.Add(this.keyLabel);
            this.cardRecording.Controls.Add(this.modifiersLabel);
            this.cardRecording.Controls.Add(this.Gif_Alt);
            this.cardRecording.Controls.Add(this.Gif_Control);
            this.cardRecording.Controls.Add(this.Gif_Shift);
            this.cardRecording.Controls.Add(this.Gif_Windows);
            this.cardRecording.Controls.Add(this.fpsHost);
            this.cardRecording.Controls.Add(this.fpsLabel);
            this.cardRecording.Controls.Add(this.secondsHost);
            this.cardRecording.Controls.Add(this.secondsLabel);
            this.cardRecording.Location = new Point(24, 104);
            this.cardRecording.Name = "cardRecording";
            this.cardRecording.Size = new Size(592, 156);
            this.cardRecording.TabIndex = 1;
            this.cardRecording.Title = "Recording";
            this.cardRecording.Description = "Hot key, frame rate and length";
            //
            // hotKeyHost
            //
            this.hotKeyHost.Controls.Add(this.Gif_Letter);
            this.hotKeyHost.Location = new Point(16, 60);
            this.hotKeyHost.Name = "hotKeyHost";
            this.hotKeyHost.Size = new Size(96, 32);
            this.hotKeyHost.TabIndex = 0;
            //
            // Gif_Letter
            //
            this.Gif_Letter.Location = new Point(10, 8);
            this.Gif_Letter.Name = "Gif_Letter";
            this.Gif_Letter.Size = new Size(76, 16);
            this.Gif_Letter.TabIndex = 0;
            this.Gif_Letter.KeyDown += new KeyEventHandler(this.Gif_Letter_KeyDown);
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
            // Gif_Alt
            //
            this.Gif_Alt.Location = new Point(200, 62);
            this.Gif_Alt.Name = "Gif_Alt";
            this.Gif_Alt.Size = new Size(88, 24);
            this.Gif_Alt.TabIndex = 1;
            this.Gif_Alt.Tag = "1";
            this.Gif_Alt.Text = "Alt";
            //
            // Gif_Control
            //
            this.Gif_Control.Location = new Point(296, 62);
            this.Gif_Control.Name = "Gif_Control";
            this.Gif_Control.Size = new Size(88, 24);
            this.Gif_Control.TabIndex = 2;
            this.Gif_Control.Tag = "2";
            this.Gif_Control.Text = "Ctrl";
            //
            // Gif_Shift
            //
            this.Gif_Shift.Location = new Point(392, 62);
            this.Gif_Shift.Name = "Gif_Shift";
            this.Gif_Shift.Size = new Size(88, 24);
            this.Gif_Shift.TabIndex = 3;
            this.Gif_Shift.Tag = "4";
            this.Gif_Shift.Text = "Shift";
            //
            // Gif_Windows
            //
            this.Gif_Windows.Location = new Point(488, 62);
            this.Gif_Windows.Name = "Gif_Windows";
            this.Gif_Windows.Size = new Size(88, 24);
            this.Gif_Windows.TabIndex = 4;
            this.Gif_Windows.Tag = "8";
            this.Gif_Windows.Text = "Win";
            //
            // fpsHost
            //
            this.fpsHost.Controls.Add(this.gifFps);
            this.fpsHost.Location = new Point(16, 104);
            this.fpsHost.Name = "fpsHost";
            this.fpsHost.Size = new Size(84, 32);
            this.fpsHost.TabIndex = 5;
            //
            // gifFps
            //
            this.gifFps.Location = new Point(10, 8);
            this.gifFps.Name = "gifFps";
            this.gifFps.Size = new Size(64, 16);
            this.gifFps.TabIndex = 0;
            //
            // fpsLabel
            //
            this.fpsLabel.AutoSize = true;
            this.fpsLabel.Location = new Point(110, 113);
            this.fpsLabel.Name = "fpsLabel";
            this.fpsLabel.Text = "frames per second (1-30)";
            //
            // secondsHost
            //
            this.secondsHost.Controls.Add(this.gifSeconds);
            this.secondsHost.Location = new Point(300, 104);
            this.secondsHost.Name = "secondsHost";
            this.secondsHost.Size = new Size(84, 32);
            this.secondsHost.TabIndex = 6;
            //
            // gifSeconds
            //
            this.gifSeconds.Location = new Point(10, 8);
            this.gifSeconds.Name = "gifSeconds";
            this.gifSeconds.Size = new Size(64, 16);
            this.gifSeconds.TabIndex = 0;
            //
            // secondsLabel
            //
            this.secondsLabel.AutoSize = true;
            this.secondsLabel.Location = new Point(394, 113);
            this.secondsLabel.Name = "secondsLabel";
            this.secondsLabel.Text = "seconds at most";
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
            this.cardLock.Location = new Point(24, 276);
            this.cardLock.Name = "cardLock";
            this.cardLock.Size = new Size(592, 144);
            this.cardLock.TabIndex = 2;
            this.cardLock.Title = "Selection lock";
            this.cardLock.Description = "Constrain the recorded area";
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
            this.lockNote.Location = new Point(16, 118);
            this.lockNote.Name = "lockNote";
            this.lockNote.Size = new Size(560, 20);
            this.lockNote.Text = "Nothing is locked.";
            //
            // cardOutput
            //
            this.cardOutput.Controls.Add(this.outputSave);
            this.cardOutput.Controls.Add(this.outputCopyPath);
            this.cardOutput.Controls.Add(this.outputOpen);
            this.cardOutput.Controls.Add(this.folderHost);
            this.cardOutput.Controls.Add(this.browseButton);
            this.cardOutput.Location = new Point(24, 436);
            this.cardOutput.Name = "cardOutput";
            this.cardOutput.Size = new Size(592, 116);
            this.cardOutput.TabIndex = 3;
            this.cardOutput.Title = "After capture";
            this.cardOutput.Description = "The file is always written to the folder below";
            //
            // outputSave
            //
            this.outputSave.Location = new Point(16, 46);
            this.outputSave.Name = "outputSave";
            this.outputSave.Size = new Size(120, 24);
            this.outputSave.TabIndex = 0;
            this.outputSave.TabStop = true;
            this.outputSave.Tag = "0";
            this.outputSave.Text = "Just save";
            //
            // outputCopyPath
            //
            this.outputCopyPath.Location = new Point(146, 46);
            this.outputCopyPath.Name = "outputCopyPath";
            this.outputCopyPath.Size = new Size(150, 24);
            this.outputCopyPath.TabIndex = 1;
            this.outputCopyPath.TabStop = true;
            this.outputCopyPath.Tag = "1";
            this.outputCopyPath.Text = "Copy the path";
            //
            // outputOpen
            //
            this.outputOpen.Location = new Point(306, 46);
            this.outputOpen.Name = "outputOpen";
            this.outputOpen.Size = new Size(140, 24);
            this.outputOpen.TabIndex = 2;
            this.outputOpen.TabStop = true;
            this.outputOpen.Tag = "2";
            this.outputOpen.Text = "Open it";
            //
            // folderHost
            //
            this.folderHost.Controls.Add(this.folderBox);
            this.folderHost.Location = new Point(16, 74);
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
            this.browseButton.Location = new Point(496, 74);
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
            this.cancelButton.TabIndex = 5;
            this.cancelButton.Text = "Cancel";
            //
            // saveButton
            //
            this.saveButton.Accent = true;
            this.saveButton.DialogResult = DialogResult.OK;
            this.saveButton.Location = new Point(536, 564);
            this.saveButton.Name = "saveButton";
            this.saveButton.Size = new Size(80, 36);
            this.saveButton.TabIndex = 6;
            this.saveButton.Text = "Save";
            this.saveButton.Click += new System.EventHandler(this.Save_Click);
            //
            // GifSettingsForm
            //
            this.AcceptButton = this.saveButton;
            this.CancelButton = this.cancelButton;
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(640, 612);
            this.Controls.Add(this.cardRecording);
            this.Controls.Add(this.cardLock);
            this.Controls.Add(this.cardOutput);
            this.Controls.Add(this.cancelButton);
            this.Controls.Add(this.saveButton);
            this.Controls.Add(this.headerBar);
            this.Font = new Font("Segoe UI", 9F);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "GifSettingsForm";
            this.SizeGripStyle = SizeGripStyle.Hide;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "MicroApp GIF Setting";
            ((System.ComponentModel.ISupportInitialize)(this.iconBox)).EndInit();
            this.headerBar.ResumeLayout(false);
            this.headerBar.PerformLayout();
            this.cardRecording.ResumeLayout(false);
            this.cardRecording.PerformLayout();
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

        private Card cardRecording;
        private FieldHost hotKeyHost;
        private TextBox Gif_Letter;
        private Label keyLabel;
        private Label modifiersLabel;
        private ModernCheckBox Gif_Alt;
        private ModernCheckBox Gif_Control;
        private ModernCheckBox Gif_Shift;
        private ModernCheckBox Gif_Windows;
        private FieldHost fpsHost;
        private TextBox gifFps;
        private Label fpsLabel;
        private FieldHost secondsHost;
        private TextBox gifSeconds;
        private Label secondsLabel;

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
        private ModernRadioButton outputSave;
        private ModernRadioButton outputCopyPath;
        private ModernRadioButton outputOpen;
        private FieldHost folderHost;
        private TextBox folderBox;
        private ModernButton browseButton;

        private ModernButton cancelButton;
        private ModernButton saveButton;
    }
}
