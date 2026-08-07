using System.Drawing;
using System.Windows.Forms;

namespace MicroApp
{
    partial class NoteSettingsForm
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
        /// Two columns, 984 x 612. Note Setting carries more than the other settings
        /// windows, and a single 640-wide column ran off the bottom of a laptop screen -
        /// so the cards sit side by side and the window always fits without scrolling.
        /// </summary>
        private void InitializeComponent()
        {
            this.headerBar = new HeaderBar();
            this.iconBox = new PictureBox();
            this.titleLabel = new Label();
            this.subtitleLabel = new Label();

            this.cardNote = new Card();
            this.hotKeyHost = new FieldHost();
            this.Note_Letter = new TextBox();
            this.keyLabel = new Label();
            this.modifiersLabel = new Label();
            this.Note_Alt = new ModernCheckBox();
            this.Note_Control = new ModernCheckBox();
            this.Note_Shift = new ModernCheckBox();
            this.Note_Windows = new ModernCheckBox();
            this.hideTaskbarCheck = new ModernCheckBox();
            this.monoFontCheck = new ModernCheckBox();

            this.cardDates = new Card();
            this.dateHost = new FieldHost();
            this.dateBox = new TextBox();
            this.dateLabel = new Label();
            this.longDateHost = new FieldHost();
            this.longDateBox = new TextBox();
            this.longDateLabel = new Label();
            this.timeHost = new FieldHost();
            this.timeBox = new TextBox();
            this.timeLabel = new Label();

            this.cardAi = new Card();
            this.providerLabel = new Label();
            this.providerHost = new FieldHost();
            this.providerBox = new ComboBox();
            this.modelLabel = new Label();
            this.modelHost = new FieldHost();
            this.modelBox = new TextBox();
            this.baseUrlLabel = new Label();
            this.baseUrlHost = new FieldHost();
            this.baseUrlBox = new TextBox();
            this.apiKeyLabel = new Label();
            this.keyHost = new FieldHost();
            this.apiKeyBox = new TextBox();
            this.banglaLabel = new Label();
            this.banglaHost = new FieldHost();
            this.banglaTokenBox = new TextBox();

            this.cardSync = new Card();
            this.syncStatusLabel = new Label();
            this.syncButton = new ModernButton();
            this.syncCodeButton = new ModernButton();
            this.disconnectButton = new ModernButton();

            this.cancelButton = new ModernButton();
            this.saveButton = new ModernButton();

            ((System.ComponentModel.ISupportInitialize)(this.iconBox)).BeginInit();
            this.headerBar.SuspendLayout();
            this.cardNote.SuspendLayout();
            this.cardDates.SuspendLayout();
            this.cardAi.SuspendLayout();
            this.cardSync.SuspendLayout();
            this.SuspendLayout();

            //
            // headerBar
            //
            this.headerBar.Controls.Add(this.iconBox);
            this.headerBar.Controls.Add(this.titleLabel);
            this.headerBar.Controls.Add(this.subtitleLabel);
            this.headerBar.Dock = DockStyle.Top;
            this.headerBar.Name = "headerBar";
            this.headerBar.Size = new Size(984, 84);
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
            this.titleLabel.Text = "Note Setting";
            //
            // subtitleLabel
            //
            this.subtitleLabel.AutoSize = true;
            this.subtitleLabel.BackColor = Color.Transparent;
            this.subtitleLabel.Location = new Point(78, 47);
            this.subtitleLabel.Name = "subtitleLabel";
            this.subtitleLabel.Text = "A quick scratch pad: every hot key press opens a fresh note";
            //
            // cardNote
            //
            this.cardNote.Controls.Add(this.hotKeyHost);
            this.cardNote.Controls.Add(this.keyLabel);
            this.cardNote.Controls.Add(this.modifiersLabel);
            this.cardNote.Controls.Add(this.Note_Alt);
            this.cardNote.Controls.Add(this.Note_Control);
            this.cardNote.Controls.Add(this.Note_Shift);
            this.cardNote.Controls.Add(this.Note_Windows);
            this.cardNote.Controls.Add(this.hideTaskbarCheck);
            this.cardNote.Controls.Add(this.monoFontCheck);
            this.cardNote.Location = new Point(24, 104);
            this.cardNote.Name = "cardNote";
            this.cardNote.Size = new Size(936, 132);
            this.cardNote.TabIndex = 1;
            this.cardNote.Title = "Note";
            this.cardNote.Description = "Hot key that opens a new note";
            //
            // hotKeyHost
            //
            this.hotKeyHost.Controls.Add(this.Note_Letter);
            this.hotKeyHost.Location = new Point(16, 56);
            this.hotKeyHost.Name = "hotKeyHost";
            this.hotKeyHost.Size = new Size(96, 32);
            this.hotKeyHost.TabIndex = 0;
            //
            // Note_Letter
            //
            this.Note_Letter.Location = new Point(10, 8);
            this.Note_Letter.Name = "Note_Letter";
            this.Note_Letter.Size = new Size(76, 16);
            this.Note_Letter.TabIndex = 0;
            this.Note_Letter.KeyDown += new KeyEventHandler(this.Note_Letter_KeyDown);
            //
            // keyLabel
            //
            this.keyLabel.AutoSize = true;
            this.keyLabel.Location = new Point(120, 65);
            this.keyLabel.Name = "keyLabel";
            this.keyLabel.Text = "key";
            //
            // modifiersLabel
            //
            this.modifiersLabel.AutoSize = true;
            this.modifiersLabel.Location = new Point(176, 40);
            this.modifiersLabel.Name = "modifiersLabel";
            this.modifiersLabel.Text = "HELD WITH";
            //
            // Note_Alt
            //
            this.Note_Alt.Location = new Point(176, 58);
            this.Note_Alt.Name = "Note_Alt";
            this.Note_Alt.Size = new Size(88, 24);
            this.Note_Alt.TabIndex = 1;
            this.Note_Alt.Tag = "1";
            this.Note_Alt.Text = "Alt";
            //
            // Note_Control
            //
            this.Note_Control.Location = new Point(272, 58);
            this.Note_Control.Name = "Note_Control";
            this.Note_Control.Size = new Size(88, 24);
            this.Note_Control.TabIndex = 2;
            this.Note_Control.Tag = "2";
            this.Note_Control.Text = "Ctrl";
            //
            // Note_Shift
            //
            this.Note_Shift.Location = new Point(368, 58);
            this.Note_Shift.Name = "Note_Shift";
            this.Note_Shift.Size = new Size(88, 24);
            this.Note_Shift.TabIndex = 3;
            this.Note_Shift.Tag = "4";
            this.Note_Shift.Text = "Shift";
            //
            // Note_Windows
            //
            this.Note_Windows.Location = new Point(464, 58);
            this.Note_Windows.Name = "Note_Windows";
            this.Note_Windows.Size = new Size(88, 24);
            this.Note_Windows.TabIndex = 4;
            this.Note_Windows.Tag = "8";
            this.Note_Windows.Text = "Win";
            //
            // hideTaskbarCheck
            //
            this.hideTaskbarCheck.Location = new Point(580, 58);
            this.hideTaskbarCheck.Name = "hideTaskbarCheck";
            this.hideTaskbarCheck.Size = new Size(340, 24);
            this.hideTaskbarCheck.TabIndex = 5;
            this.hideTaskbarCheck.Text = "Hide note windows from the taskbar";
            //
            // monoFontCheck
            //
            this.monoFontCheck.Location = new Point(16, 96);
            this.monoFontCheck.Name = "monoFontCheck";
            this.monoFontCheck.Size = new Size(560, 24);
            this.monoFontCheck.TabIndex = 6;
            this.monoFontCheck.Text = "Fixed-width text, like Notepad - tables and code line up";
            //
            // cardDates
            //
            this.cardDates.Controls.Add(this.dateHost);
            this.cardDates.Controls.Add(this.dateLabel);
            this.cardDates.Controls.Add(this.longDateHost);
            this.cardDates.Controls.Add(this.longDateLabel);
            this.cardDates.Controls.Add(this.timeHost);
            this.cardDates.Controls.Add(this.timeLabel);
            this.cardDates.Location = new Point(24, 252);
            this.cardDates.Name = "cardDates";
            this.cardDates.Size = new Size(460, 164);
            this.cardDates.TabIndex = 2;
            this.cardDates.Title = "Date && time";
            this.cardDates.Description = "Formats for the toolbar insert buttons";
            //
            // dateHost
            //
            this.dateHost.Controls.Add(this.dateBox);
            this.dateHost.Location = new Point(16, 56);
            this.dateHost.Name = "dateHost";
            this.dateHost.Size = new Size(180, 32);
            this.dateHost.TabIndex = 0;
            //
            // dateBox
            //
            this.dateBox.Location = new Point(10, 8);
            this.dateBox.Name = "dateBox";
            this.dateBox.Size = new Size(160, 16);
            this.dateBox.TabIndex = 0;
            this.dateBox.TextChanged += new System.EventHandler(this.Format_TextChanged);
            //
            // dateLabel
            //
            this.dateLabel.AutoSize = true;
            this.dateLabel.Location = new Point(206, 65);
            this.dateLabel.Name = "dateLabel";
            this.dateLabel.Text = "date";
            //
            // longDateHost
            //
            this.longDateHost.Controls.Add(this.longDateBox);
            this.longDateHost.Location = new Point(16, 92);
            this.longDateHost.Name = "longDateHost";
            this.longDateHost.Size = new Size(180, 32);
            this.longDateHost.TabIndex = 1;
            //
            // longDateBox
            //
            this.longDateBox.Location = new Point(10, 8);
            this.longDateBox.Name = "longDateBox";
            this.longDateBox.Size = new Size(160, 16);
            this.longDateBox.TabIndex = 0;
            this.longDateBox.TextChanged += new System.EventHandler(this.Format_TextChanged);
            //
            // longDateLabel
            //
            this.longDateLabel.AutoSize = true;
            this.longDateLabel.Location = new Point(206, 101);
            this.longDateLabel.Name = "longDateLabel";
            this.longDateLabel.Text = "long date";
            //
            // timeHost
            //
            this.timeHost.Controls.Add(this.timeBox);
            this.timeHost.Location = new Point(16, 128);
            this.timeHost.Name = "timeHost";
            this.timeHost.Size = new Size(180, 32);
            this.timeHost.TabIndex = 2;
            //
            // timeBox
            //
            this.timeBox.Location = new Point(10, 8);
            this.timeBox.Name = "timeBox";
            this.timeBox.Size = new Size(160, 16);
            this.timeBox.TabIndex = 0;
            this.timeBox.TextChanged += new System.EventHandler(this.Format_TextChanged);
            //
            // timeLabel
            //
            this.timeLabel.AutoSize = true;
            this.timeLabel.Location = new Point(206, 137);
            this.timeLabel.Name = "timeLabel";
            this.timeLabel.Text = "timestamp";
            //
            // cardAi
            //
            this.cardAi.Controls.Add(this.providerLabel);
            this.cardAi.Controls.Add(this.providerHost);
            this.cardAi.Controls.Add(this.modelLabel);
            this.cardAi.Controls.Add(this.modelHost);
            this.cardAi.Controls.Add(this.baseUrlLabel);
            this.cardAi.Controls.Add(this.baseUrlHost);
            this.cardAi.Controls.Add(this.apiKeyLabel);
            this.cardAi.Controls.Add(this.keyHost);
            this.cardAi.Controls.Add(this.banglaLabel);
            this.cardAi.Controls.Add(this.banglaHost);
            this.cardAi.Location = new Point(500, 252);
            this.cardAi.Name = "cardAi";
            this.cardAi.Size = new Size(460, 230);
            this.cardAi.TabIndex = 3;
            this.cardAi.Title = "AI && Bangla";
            this.cardAi.Description = "Keys for the Grammar tools and Bangla phonetic typing";
            //
            // providerLabel
            //
            this.providerLabel.AutoSize = true;
            this.providerLabel.Location = new Point(16, 48);
            this.providerLabel.Name = "providerLabel";
            this.providerLabel.Text = "PROVIDER";
            //
            // providerHost
            //
            this.providerHost.Controls.Add(this.providerBox);
            this.providerHost.Location = new Point(16, 66);
            this.providerHost.Name = "providerHost";
            this.providerHost.Size = new Size(150, 32);
            this.providerHost.TabIndex = 0;
            //
            // providerBox
            //
            this.providerBox.Location = new Point(10, 6);
            this.providerBox.Name = "providerBox";
            this.providerBox.Size = new Size(130, 21);
            this.providerBox.TabIndex = 0;
            this.providerBox.SelectedIndexChanged += new System.EventHandler(this.Provider_Changed);
            //
            // modelLabel
            //
            this.modelLabel.AutoSize = true;
            this.modelLabel.Location = new Point(176, 48);
            this.modelLabel.Name = "modelLabel";
            this.modelLabel.Text = "MODEL";
            //
            // modelHost
            //
            this.modelHost.Controls.Add(this.modelBox);
            this.modelHost.Location = new Point(176, 66);
            this.modelHost.Name = "modelHost";
            this.modelHost.Size = new Size(268, 32);
            this.modelHost.TabIndex = 1;
            //
            // modelBox
            //
            this.modelBox.Location = new Point(10, 8);
            this.modelBox.Name = "modelBox";
            this.modelBox.Size = new Size(248, 16);
            this.modelBox.TabIndex = 0;
            //
            // baseUrlLabel
            //
            this.baseUrlLabel.AutoSize = true;
            this.baseUrlLabel.Location = new Point(16, 108);
            this.baseUrlLabel.Name = "baseUrlLabel";
            this.baseUrlLabel.Text = "BASE URL (MIMO ONLY)";
            //
            // baseUrlHost
            //
            this.baseUrlHost.Controls.Add(this.baseUrlBox);
            this.baseUrlHost.Location = new Point(16, 126);
            this.baseUrlHost.Name = "baseUrlHost";
            this.baseUrlHost.Size = new Size(428, 32);
            this.baseUrlHost.TabIndex = 2;
            //
            // baseUrlBox
            //
            this.baseUrlBox.Location = new Point(10, 8);
            this.baseUrlBox.Name = "baseUrlBox";
            this.baseUrlBox.Size = new Size(408, 16);
            this.baseUrlBox.TabIndex = 0;
            //
            // apiKeyLabel
            //
            this.apiKeyLabel.AutoSize = true;
            this.apiKeyLabel.Location = new Point(16, 168);
            this.apiKeyLabel.Name = "apiKeyLabel";
            this.apiKeyLabel.Text = "AI API KEY";
            //
            // keyHost
            //
            this.keyHost.Controls.Add(this.apiKeyBox);
            this.keyHost.Location = new Point(16, 186);
            this.keyHost.Name = "keyHost";
            this.keyHost.Size = new Size(200, 32);
            this.keyHost.TabIndex = 3;
            //
            // apiKeyBox
            //
            this.apiKeyBox.Location = new Point(10, 8);
            this.apiKeyBox.Name = "apiKeyBox";
            this.apiKeyBox.Size = new Size(180, 16);
            this.apiKeyBox.TabIndex = 0;
            this.apiKeyBox.UseSystemPasswordChar = true;
            //
            // banglaLabel
            //
            this.banglaLabel.AutoSize = true;
            this.banglaLabel.Location = new Point(226, 168);
            this.banglaLabel.Name = "banglaLabel";
            this.banglaLabel.Text = "BANGLA TOKEN (STRING.BD)";
            //
            // banglaHost
            //
            this.banglaHost.Controls.Add(this.banglaTokenBox);
            this.banglaHost.Location = new Point(226, 186);
            this.banglaHost.Name = "banglaHost";
            this.banglaHost.Size = new Size(218, 32);
            this.banglaHost.TabIndex = 4;
            //
            // banglaTokenBox
            //
            this.banglaTokenBox.Location = new Point(10, 8);
            this.banglaTokenBox.Name = "banglaTokenBox";
            this.banglaTokenBox.Size = new Size(198, 16);
            this.banglaTokenBox.TabIndex = 0;
            this.banglaTokenBox.UseSystemPasswordChar = true;
            //
            // cardSync
            //
            this.cardSync.Controls.Add(this.syncStatusLabel);
            this.cardSync.Controls.Add(this.syncButton);
            this.cardSync.Controls.Add(this.syncCodeButton);
            this.cardSync.Controls.Add(this.disconnectButton);
            this.cardSync.Location = new Point(24, 432);
            this.cardSync.Name = "cardSync";
            this.cardSync.Size = new Size(460, 112);
            this.cardSync.TabIndex = 4;
            this.cardSync.Title = "Sync";
            this.cardSync.Description = "Optional: the same notes on every PC, in a free database of your own";
            //
            // syncStatusLabel
            //
            this.syncStatusLabel.AutoSize = false;
            this.syncStatusLabel.Location = new Point(16, 48);
            this.syncStatusLabel.Size = new Size(428, 22);
            this.syncStatusLabel.Name = "syncStatusLabel";
            this.syncStatusLabel.Text = "";
            //
            // syncButton
            //
            this.syncButton.Location = new Point(16, 74);
            this.syncButton.Name = "syncButton";
            this.syncButton.Size = new Size(130, 34);
            this.syncButton.TabIndex = 0;
            this.syncButton.Text = "Set up sync";
            this.syncButton.Click += new System.EventHandler(this.Sync_Click);
            //
            // syncCodeButton
            //
            this.syncCodeButton.Location = new Point(158, 74);
            this.syncCodeButton.Name = "syncCodeButton";
            this.syncCodeButton.Size = new Size(110, 34);
            this.syncCodeButton.TabIndex = 1;
            this.syncCodeButton.Text = "Sync code";
            this.syncCodeButton.Click += new System.EventHandler(this.SyncCode_Click);
            //
            // disconnectButton
            //
            this.disconnectButton.Location = new Point(280, 74);
            this.disconnectButton.Name = "disconnectButton";
            this.disconnectButton.Size = new Size(110, 34);
            this.disconnectButton.TabIndex = 2;
            this.disconnectButton.Text = "Disconnect";
            this.disconnectButton.Click += new System.EventHandler(this.Disconnect_Click);
            //
            // cancelButton
            //
            this.cancelButton.DialogResult = DialogResult.Cancel;
            this.cancelButton.Location = new Point(780, 564);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new Size(88, 36);
            this.cancelButton.TabIndex = 5;
            this.cancelButton.Text = "Cancel";
            //
            // saveButton
            //
            this.saveButton.Accent = true;
            this.saveButton.DialogResult = DialogResult.OK;
            this.saveButton.Location = new Point(880, 564);
            this.saveButton.Name = "saveButton";
            this.saveButton.Size = new Size(80, 36);
            this.saveButton.TabIndex = 6;
            this.saveButton.Text = "Save";
            this.saveButton.Click += new System.EventHandler(this.Save_Click);
            //
            // NoteSettingsForm
            //
            this.AcceptButton = this.saveButton;
            this.CancelButton = this.cancelButton;
            this.AutoScaleDimensions = new SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(984, 612);
            this.Controls.Add(this.cardNote);
            this.Controls.Add(this.cardDates);
            this.Controls.Add(this.cardAi);
            this.Controls.Add(this.cardSync);
            this.Controls.Add(this.cancelButton);
            this.Controls.Add(this.saveButton);
            this.Controls.Add(this.headerBar);
            this.Font = new Font("Segoe UI", 9F);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "NoteSettingsForm";
            this.SizeGripStyle = SizeGripStyle.Hide;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "MicroApp Note Setting";
            ((System.ComponentModel.ISupportInitialize)(this.iconBox)).EndInit();
            this.headerBar.ResumeLayout(false);
            this.headerBar.PerformLayout();
            this.cardNote.ResumeLayout(false);
            this.cardNote.PerformLayout();
            this.cardDates.ResumeLayout(false);
            this.cardDates.PerformLayout();
            this.cardAi.ResumeLayout(false);
            this.cardAi.PerformLayout();
            this.cardSync.ResumeLayout(false);
            this.cardSync.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion

        private HeaderBar headerBar;
        private PictureBox iconBox;
        private Label titleLabel;
        private Label subtitleLabel;

        private Card cardNote;
        private FieldHost hotKeyHost;
        private TextBox Note_Letter;
        private Label keyLabel;
        private Label modifiersLabel;
        private ModernCheckBox Note_Alt;
        private ModernCheckBox Note_Control;
        private ModernCheckBox Note_Shift;
        private ModernCheckBox Note_Windows;
        private ModernCheckBox hideTaskbarCheck;
        private ModernCheckBox monoFontCheck;

        private Card cardDates;
        private FieldHost dateHost;
        private TextBox dateBox;
        private Label dateLabel;
        private FieldHost longDateHost;
        private TextBox longDateBox;
        private Label longDateLabel;
        private FieldHost timeHost;
        private TextBox timeBox;
        private Label timeLabel;

        private Card cardAi;
        private Label providerLabel;
        private FieldHost providerHost;
        private ComboBox providerBox;
        private Label modelLabel;
        private FieldHost modelHost;
        private TextBox modelBox;
        private Label baseUrlLabel;
        private FieldHost baseUrlHost;
        private TextBox baseUrlBox;
        private Label apiKeyLabel;
        private FieldHost keyHost;
        private TextBox apiKeyBox;
        private Label banglaLabel;
        private FieldHost banglaHost;
        private TextBox banglaTokenBox;

        private Card cardSync;
        private Label syncStatusLabel;
        private ModernButton syncButton;
        private ModernButton syncCodeButton;
        private ModernButton disconnectButton;

        private ModernButton cancelButton;
        private ModernButton saveButton;
    }
}
