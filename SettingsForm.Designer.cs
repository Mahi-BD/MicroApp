using System.Drawing;
using System.Windows.Forms;

namespace MicroApp
{
    partial class SettingsForm
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
        /// Modern two-column settings layout: header band, cards for each group of
        /// settings, and a footer with the primary action.
        /// </summary>
        private void InitializeComponent()
        {
            this.headerBar = new HeaderBar();
            this.iconBox = new PictureBox();
            this.titleLabel = new Label();
            this.subtitleLabel = new Label();
            this.versionLabel = new Pill();

            this.cardMethod = new Card();
            this.Method_Forms = new ModernRadioButton();
            this.Method_AutoIt = new ModernRadioButton();
            this.Method_ScanCode = new ModernRadioButton();

            this.cardDelays = new Card();
            this.startDelayHost = new FieldHost();
            this.startDelayMS = new TextBox();
            this.label1 = new Label();
            this.delayHost = new FieldHost();
            this.DelayMS = new TextBox();
            this.label3 = new Label();

            this.cardSafety = new Card();
            this.confirmOverActive = new ModernCheckBox();
            this.confirmOverHost = new FieldHost();
            this.confirmOver = new TextBox();
            this.label4 = new Label();

            this.cardHotKey = new Card();
            this.hotKeyHost = new FieldHost();
            this.HotKey_Letter = new TextBox();
            this.label2 = new Label();
            this.modifiersLabel = new Label();
            this.HotKey_Alt = new ModernCheckBox();
            this.HotKey_Control = new ModernCheckBox();
            this.HotKey_Shift = new ModernCheckBox();
            this.HotKey_Windows = new ModernCheckBox();
            this.modeLabel = new Label();
            this.hotKeyModeTarget = new ModernRadioButton();
            this.hotKeyModeType = new ModernRadioButton();

            this.cardTips = new Card();
            this.tipsLabel = new Label();

            this.cancelButton = new ModernButton();
            this.Done = new ModernButton();

            ((System.ComponentModel.ISupportInitialize)(this.iconBox)).BeginInit();
            this.headerBar.SuspendLayout();
            this.cardMethod.SuspendLayout();
            this.cardDelays.SuspendLayout();
            this.cardSafety.SuspendLayout();
            this.cardHotKey.SuspendLayout();
            this.cardTips.SuspendLayout();
            this.SuspendLayout();

            //
            // headerBar
            //
            this.headerBar.Controls.Add(this.iconBox);
            this.headerBar.Controls.Add(this.titleLabel);
            this.headerBar.Controls.Add(this.subtitleLabel);
            this.headerBar.Controls.Add(this.versionLabel);
            this.headerBar.Dock = DockStyle.Top;
            this.headerBar.Name = "headerBar";
            this.headerBar.Size = new Size(640, 84);
            this.headerBar.TabIndex = 0;
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
            this.titleLabel.Text = "MicroApp";
            //
            // subtitleLabel
            //
            this.subtitleLabel.AutoSize = true;
            this.subtitleLabel.BackColor = Color.Transparent;
            this.subtitleLabel.Location = new Point(78, 47);
            this.subtitleLabel.Name = "subtitleLabel";
            this.subtitleLabel.Text = "Types the clipboard wherever you click";
            //
            // versionLabel
            //
            this.versionLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            this.versionLabel.BackColor = Color.Transparent;
            this.versionLabel.Location = new Point(548, 30);
            this.versionLabel.Name = "versionLabel";
            this.versionLabel.Size = new Size(68, 24);
            this.versionLabel.Text = "v0.0.0";
            //
            // cardMethod
            //
            this.cardMethod.Controls.Add(this.Method_Forms);
            this.cardMethod.Controls.Add(this.Method_AutoIt);
            this.cardMethod.Controls.Add(this.Method_ScanCode);
            this.cardMethod.Location = new Point(24, 104);
            this.cardMethod.Name = "cardMethod";
            this.cardMethod.Size = new Size(356, 152);
            this.cardMethod.TabIndex = 1;
            this.cardMethod.Title = "Typing method";
            this.cardMethod.Description = "How characters reach the target window";
            //
            // Method_Forms
            //
            this.Method_Forms.Location = new Point(16, 64);
            this.Method_Forms.Name = "Method_Forms";
            this.Method_Forms.Size = new Size(324, 24);
            this.Method_Forms.TabIndex = 0;
            this.Method_Forms.TabStop = true;
            this.Method_Forms.Tag = "0";
            this.Method_Forms.Text = "SendKeys  (classic, fastest)";
            //
            // Method_AutoIt
            //
            this.Method_AutoIt.Location = new Point(16, 92);
            this.Method_AutoIt.Name = "Method_AutoIt";
            this.Method_AutoIt.Size = new Size(324, 24);
            this.Method_AutoIt.TabIndex = 1;
            this.Method_AutoIt.TabStop = true;
            this.Method_AutoIt.Tag = "1";
            this.Method_AutoIt.Text = "AutoIt Send  (handles more layouts)";
            //
            // Method_ScanCode
            //
            this.Method_ScanCode.Location = new Point(16, 120);
            this.Method_ScanCode.Name = "Method_ScanCode";
            this.Method_ScanCode.Size = new Size(324, 24);
            this.Method_ScanCode.TabIndex = 2;
            this.Method_ScanCode.Tag = "3";
            this.Method_ScanCode.Text = "SendInput  (works in VM consoles)";
            //
            // cardDelays
            //
            this.cardDelays.Controls.Add(this.startDelayHost);
            this.cardDelays.Controls.Add(this.label1);
            this.cardDelays.Controls.Add(this.delayHost);
            this.cardDelays.Controls.Add(this.label3);
            this.cardDelays.Location = new Point(24, 272);
            this.cardDelays.Name = "cardDelays";
            this.cardDelays.Size = new Size(356, 140);
            this.cardDelays.TabIndex = 2;
            this.cardDelays.Title = "Delays";
            this.cardDelays.Description = "Give the target window time to keep up";
            //
            // startDelayHost
            //
            this.startDelayHost.Controls.Add(this.startDelayMS);
            this.startDelayHost.Location = new Point(16, 60);
            this.startDelayHost.Name = "startDelayHost";
            this.startDelayHost.Size = new Size(84, 32);
            this.startDelayHost.TabIndex = 0;
            //
            // startDelayMS
            //
            this.startDelayMS.Location = new Point(10, 8);
            this.startDelayMS.Name = "startDelayMS";
            this.startDelayMS.Size = new Size(64, 16);
            this.startDelayMS.TabIndex = 0;
            //
            // label1
            //
            this.label1.AutoSize = true;
            this.label1.Location = new Point(110, 69);
            this.label1.Name = "label1";
            this.label1.Text = "milliseconds before typing starts";
            //
            // delayHost
            //
            this.delayHost.Controls.Add(this.DelayMS);
            this.delayHost.Location = new Point(16, 98);
            this.delayHost.Name = "delayHost";
            this.delayHost.Size = new Size(84, 32);
            this.delayHost.TabIndex = 1;
            //
            // DelayMS
            //
            this.DelayMS.Location = new Point(10, 8);
            this.DelayMS.Name = "DelayMS";
            this.DelayMS.Size = new Size(64, 16);
            this.DelayMS.TabIndex = 0;
            //
            // label3
            //
            this.label3.AutoSize = true;
            this.label3.Location = new Point(110, 107);
            this.label3.Name = "label3";
            this.label3.Text = "milliseconds between keystrokes";
            //
            // cardSafety
            //
            this.cardSafety.Controls.Add(this.confirmOverActive);
            this.cardSafety.Controls.Add(this.confirmOverHost);
            this.cardSafety.Controls.Add(this.label4);
            this.cardSafety.Location = new Point(24, 428);
            this.cardSafety.Name = "cardSafety";
            this.cardSafety.Size = new Size(356, 124);
            this.cardSafety.TabIndex = 3;
            this.cardSafety.Title = "Safety";
            //
            // confirmOverActive
            //
            this.confirmOverActive.Location = new Point(16, 44);
            this.confirmOverActive.Name = "confirmOverActive";
            this.confirmOverActive.Size = new Size(324, 24);
            this.confirmOverActive.TabIndex = 0;
            this.confirmOverActive.Text = "Ask me first when pasting more than";
            this.confirmOverActive.CheckedChanged += new System.EventHandler(this.confirmOverActive_CheckedChanged);
            //
            // confirmOverHost
            //
            this.confirmOverHost.Controls.Add(this.confirmOver);
            this.confirmOverHost.Location = new Point(16, 76);
            this.confirmOverHost.Name = "confirmOverHost";
            this.confirmOverHost.Size = new Size(84, 32);
            this.confirmOverHost.TabIndex = 1;
            //
            // confirmOver
            //
            this.confirmOver.Location = new Point(10, 8);
            this.confirmOver.Name = "confirmOver";
            this.confirmOver.Size = new Size(64, 16);
            this.confirmOver.TabIndex = 0;
            //
            // label4
            //
            this.label4.AutoSize = true;
            this.label4.Location = new Point(110, 85);
            this.label4.Name = "label4";
            this.label4.Text = "keystrokes";
            //
            // cardHotKey
            //
            this.cardHotKey.Controls.Add(this.hotKeyHost);
            this.cardHotKey.Controls.Add(this.label2);
            this.cardHotKey.Controls.Add(this.modifiersLabel);
            this.cardHotKey.Controls.Add(this.HotKey_Alt);
            this.cardHotKey.Controls.Add(this.HotKey_Control);
            this.cardHotKey.Controls.Add(this.HotKey_Shift);
            this.cardHotKey.Controls.Add(this.HotKey_Windows);
            this.cardHotKey.Controls.Add(this.modeLabel);
            this.cardHotKey.Controls.Add(this.hotKeyModeTarget);
            this.cardHotKey.Controls.Add(this.hotKeyModeType);
            this.cardHotKey.Location = new Point(396, 104);
            this.cardHotKey.Name = "cardHotKey";
            this.cardHotKey.Size = new Size(220, 268);
            this.cardHotKey.TabIndex = 4;
            this.cardHotKey.Title = "Hot key";
            this.cardHotKey.Description = "Trigger without the tray icon";
            //
            // hotKeyHost
            //
            this.hotKeyHost.Controls.Add(this.HotKey_Letter);
            this.hotKeyHost.Location = new Point(16, 60);
            this.hotKeyHost.Name = "hotKeyHost";
            this.hotKeyHost.Size = new Size(96, 32);
            this.hotKeyHost.TabIndex = 0;
            //
            // HotKey_Letter
            //
            this.HotKey_Letter.Location = new Point(10, 8);
            this.HotKey_Letter.Name = "HotKey_Letter";
            this.HotKey_Letter.Size = new Size(76, 16);
            this.HotKey_Letter.TabIndex = 0;
            this.HotKey_Letter.KeyDown += new KeyEventHandler(this.HotKey_Letter_KeyDown);
            //
            // label2
            //
            this.label2.AutoSize = true;
            this.label2.Location = new Point(120, 69);
            this.label2.Name = "label2";
            this.label2.Text = "key";
            //
            // modifiersLabel
            //
            this.modifiersLabel.AutoSize = true;
            this.modifiersLabel.Location = new Point(16, 104);
            this.modifiersLabel.Name = "modifiersLabel";
            this.modifiersLabel.Text = "HELD WITH";
            //
            // HotKey_Alt
            //
            this.HotKey_Alt.Location = new Point(16, 124);
            this.HotKey_Alt.Name = "HotKey_Alt";
            this.HotKey_Alt.Size = new Size(90, 24);
            this.HotKey_Alt.TabIndex = 1;
            this.HotKey_Alt.Tag = "1";
            this.HotKey_Alt.Text = "Alt";
            //
            // HotKey_Control
            //
            this.HotKey_Control.Location = new Point(112, 124);
            this.HotKey_Control.Name = "HotKey_Control";
            this.HotKey_Control.Size = new Size(94, 24);
            this.HotKey_Control.TabIndex = 2;
            this.HotKey_Control.Tag = "2";
            this.HotKey_Control.Text = "Ctrl";
            //
            // HotKey_Shift
            //
            this.HotKey_Shift.Location = new Point(16, 152);
            this.HotKey_Shift.Name = "HotKey_Shift";
            this.HotKey_Shift.Size = new Size(90, 24);
            this.HotKey_Shift.TabIndex = 3;
            this.HotKey_Shift.Tag = "4";
            this.HotKey_Shift.Text = "Shift";
            //
            // HotKey_Windows
            //
            this.HotKey_Windows.Location = new Point(112, 152);
            this.HotKey_Windows.Name = "HotKey_Windows";
            this.HotKey_Windows.Size = new Size(94, 24);
            this.HotKey_Windows.TabIndex = 4;
            this.HotKey_Windows.Tag = "8";
            this.HotKey_Windows.Text = "Win";
            //
            // modeLabel
            //
            this.modeLabel.AutoSize = true;
            this.modeLabel.Location = new Point(16, 186);
            this.modeLabel.Name = "modeLabel";
            this.modeLabel.Text = "WHEN PRESSED";
            //
            // hotKeyModeTarget
            //
            this.hotKeyModeTarget.Location = new Point(16, 206);
            this.hotKeyModeTarget.Name = "hotKeyModeTarget";
            this.hotKeyModeTarget.Size = new Size(190, 24);
            this.hotKeyModeTarget.TabIndex = 5;
            this.hotKeyModeTarget.TabStop = true;
            this.hotKeyModeTarget.Tag = "0";
            this.hotKeyModeTarget.Text = "Let me click a target";
            //
            // hotKeyModeType
            //
            this.hotKeyModeType.Location = new Point(16, 232);
            this.hotKeyModeType.Name = "hotKeyModeType";
            this.hotKeyModeType.Size = new Size(190, 24);
            this.hotKeyModeType.TabIndex = 6;
            this.hotKeyModeType.TabStop = true;
            this.hotKeyModeType.Tag = "1";
            this.hotKeyModeType.Text = "Start typing right away";
            //
            // cardTips
            //
            this.cardTips.Controls.Add(this.tipsLabel);
            this.cardTips.Location = new Point(396, 388);
            this.cardTips.Name = "cardTips";
            this.cardTips.Size = new Size(220, 164);
            this.cardTips.TabIndex = 5;
            this.cardTips.TabStop = false;
            this.cardTips.Title = "How it works";
            //
            // tipsLabel
            //
            this.tipsLabel.Location = new Point(16, 44);
            this.tipsLabel.Name = "tipsLabel";
            this.tipsLabel.Size = new Size(190, 108);
            this.tipsLabel.Text = "1.  Copy some text.\r\n2.  Click the tray icon, or press your hot key.\r\n3.  Click where it should be typed.\r\n\r\nPress Esc to stop typing.";
            //
            // cancelButton
            //
            this.cancelButton.DialogResult = DialogResult.Cancel;
            this.cancelButton.Location = new Point(436, 564);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new Size(88, 36);
            this.cancelButton.TabIndex = 6;
            this.cancelButton.Text = "Cancel";
            //
            // Done
            //
            this.Done.Accent = true;
            this.Done.DialogResult = DialogResult.OK;
            this.Done.Location = new Point(536, 564);
            this.Done.Name = "Done";
            this.Done.Size = new Size(80, 36);
            this.Done.TabIndex = 7;
            this.Done.Text = "Save";
            this.Done.Click += new System.EventHandler(this.Done_Click);
            //
            // SettingsForm
            //
            this.AcceptButton = this.Done;
            this.CancelButton = this.cancelButton;
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(640, 612);
            this.Controls.Add(this.cardMethod);
            this.Controls.Add(this.cardDelays);
            this.Controls.Add(this.cardSafety);
            this.Controls.Add(this.cardHotKey);
            this.Controls.Add(this.cardTips);
            this.Controls.Add(this.cancelButton);
            this.Controls.Add(this.Done);
            this.Controls.Add(this.headerBar);
            this.Font = new Font("Segoe UI", 9F);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "SettingsForm";
            this.SizeGripStyle = SizeGripStyle.Hide;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "MicroApp Settings";
            ((System.ComponentModel.ISupportInitialize)(this.iconBox)).EndInit();
            this.headerBar.ResumeLayout(false);
            this.headerBar.PerformLayout();
            this.cardMethod.ResumeLayout(false);
            this.cardDelays.ResumeLayout(false);
            this.cardDelays.PerformLayout();
            this.cardSafety.ResumeLayout(false);
            this.cardSafety.PerformLayout();
            this.cardHotKey.ResumeLayout(false);
            this.cardHotKey.PerformLayout();
            this.cardTips.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private HeaderBar headerBar;
        private PictureBox iconBox;
        private Label titleLabel;
        private Label subtitleLabel;
        private Pill versionLabel;

        private Card cardMethod;
        private ModernRadioButton Method_Forms;
        private ModernRadioButton Method_AutoIt;
        private ModernRadioButton Method_ScanCode;

        private Card cardDelays;
        private FieldHost startDelayHost;
        private TextBox startDelayMS;
        private Label label1;
        private FieldHost delayHost;
        private TextBox DelayMS;
        private Label label3;

        private Card cardSafety;
        private ModernCheckBox confirmOverActive;
        private FieldHost confirmOverHost;
        private TextBox confirmOver;
        private Label label4;

        private Card cardHotKey;
        private FieldHost hotKeyHost;
        private TextBox HotKey_Letter;
        private Label label2;
        private Label modifiersLabel;
        private ModernCheckBox HotKey_Alt;
        private ModernCheckBox HotKey_Control;
        private ModernCheckBox HotKey_Shift;
        private ModernCheckBox HotKey_Windows;
        private Label modeLabel;
        private ModernRadioButton hotKeyModeTarget;
        private ModernRadioButton hotKeyModeType;

        private Card cardTips;
        private Label tipsLabel;

        private ModernButton cancelButton;
        private ModernButton Done;
    }
}
