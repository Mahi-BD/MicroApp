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

        /// <summary>Same canvas as Key Setting (640 x 612).</summary>
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
            this.providerHost = new FieldHost();
            this.providerBox = new ComboBox();
            this.modelHost = new FieldHost();
            this.modelBox = new TextBox();
            this.modelLabel = new Label();
            this.baseUrlHost = new FieldHost();
            this.baseUrlBox = new TextBox();
            this.baseUrlLabel = new Label();
            this.keyHost = new FieldHost();
            this.apiKeyBox = new TextBox();
            this.apiKeyLabel = new Label();
            this.aiNote = new Label();

            this.cancelButton = new ModernButton();
            this.saveButton = new ModernButton();

            ((System.ComponentModel.ISupportInitialize)(this.iconBox)).BeginInit();
            this.headerBar.SuspendLayout();
            this.cardNote.SuspendLayout();
            this.cardDates.SuspendLayout();
            this.cardAi.SuspendLayout();
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
            this.cardNote.Location = new Point(24, 104);
            this.cardNote.Name = "cardNote";
            this.cardNote.Size = new Size(592, 136);
            this.cardNote.TabIndex = 1;
            this.cardNote.Title = "Note";
            this.cardNote.Description = "Hot key that opens a new note";
            //
            // hotKeyHost
            //
            this.hotKeyHost.Controls.Add(this.Note_Letter);
            this.hotKeyHost.Location = new Point(16, 60);
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
            // Note_Alt
            //
            this.Note_Alt.Location = new Point(200, 62);
            this.Note_Alt.Name = "Note_Alt";
            this.Note_Alt.Size = new Size(88, 24);
            this.Note_Alt.TabIndex = 1;
            this.Note_Alt.Tag = "1";
            this.Note_Alt.Text = "Alt";
            //
            // Note_Control
            //
            this.Note_Control.Location = new Point(296, 62);
            this.Note_Control.Name = "Note_Control";
            this.Note_Control.Size = new Size(88, 24);
            this.Note_Control.TabIndex = 2;
            this.Note_Control.Tag = "2";
            this.Note_Control.Text = "Ctrl";
            //
            // Note_Shift
            //
            this.Note_Shift.Location = new Point(392, 62);
            this.Note_Shift.Name = "Note_Shift";
            this.Note_Shift.Size = new Size(88, 24);
            this.Note_Shift.TabIndex = 3;
            this.Note_Shift.Tag = "4";
            this.Note_Shift.Text = "Shift";
            //
            // Note_Windows
            //
            this.Note_Windows.Location = new Point(488, 62);
            this.Note_Windows.Name = "Note_Windows";
            this.Note_Windows.Size = new Size(88, 24);
            this.Note_Windows.TabIndex = 4;
            this.Note_Windows.Tag = "8";
            this.Note_Windows.Text = "Win";
            //
            // hideTaskbarCheck
            //
            this.hideTaskbarCheck.Location = new Point(16, 102);
            this.hideTaskbarCheck.Name = "hideTaskbarCheck";
            this.hideTaskbarCheck.Size = new Size(400, 24);
            this.hideTaskbarCheck.TabIndex = 5;
            this.hideTaskbarCheck.Text = "Hide note windows from the taskbar";
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
            this.cardDates.Size = new Size(592, 172);
            this.cardDates.TabIndex = 2;
            this.cardDates.Title = "Date && time";
            this.cardDates.Description = "Formats for the toolbar insert buttons";
            //
            // dateHost
            //
            this.dateHost.Controls.Add(this.dateBox);
            this.dateHost.Location = new Point(16, 56);
            this.dateHost.Name = "dateHost";
            this.dateHost.Size = new Size(190, 32);
            this.dateHost.TabIndex = 0;
            //
            // dateBox
            //
            this.dateBox.Location = new Point(10, 8);
            this.dateBox.Name = "dateBox";
            this.dateBox.Size = new Size(170, 16);
            this.dateBox.TabIndex = 0;
            this.dateBox.TextChanged += new System.EventHandler(this.Format_TextChanged);
            //
            // dateLabel
            //
            this.dateLabel.AutoSize = true;
            this.dateLabel.Location = new Point(216, 65);
            this.dateLabel.Name = "dateLabel";
            this.dateLabel.Text = "date";
            //
            // longDateHost
            //
            this.longDateHost.Controls.Add(this.longDateBox);
            this.longDateHost.Location = new Point(16, 96);
            this.longDateHost.Name = "longDateHost";
            this.longDateHost.Size = new Size(190, 32);
            this.longDateHost.TabIndex = 1;
            //
            // longDateBox
            //
            this.longDateBox.Location = new Point(10, 8);
            this.longDateBox.Name = "longDateBox";
            this.longDateBox.Size = new Size(170, 16);
            this.longDateBox.TabIndex = 0;
            this.longDateBox.TextChanged += new System.EventHandler(this.Format_TextChanged);
            //
            // longDateLabel
            //
            this.longDateLabel.AutoSize = true;
            this.longDateLabel.Location = new Point(216, 105);
            this.longDateLabel.Name = "longDateLabel";
            this.longDateLabel.Text = "long date";
            //
            // timeHost
            //
            this.timeHost.Controls.Add(this.timeBox);
            this.timeHost.Location = new Point(16, 136);
            this.timeHost.Name = "timeHost";
            this.timeHost.Size = new Size(190, 32);
            this.timeHost.TabIndex = 2;
            //
            // timeBox
            //
            this.timeBox.Location = new Point(10, 8);
            this.timeBox.Name = "timeBox";
            this.timeBox.Size = new Size(170, 16);
            this.timeBox.TabIndex = 0;
            this.timeBox.TextChanged += new System.EventHandler(this.Format_TextChanged);
            //
            // timeLabel
            //
            this.timeLabel.AutoSize = true;
            this.timeLabel.Location = new Point(216, 145);
            this.timeLabel.Name = "timeLabel";
            this.timeLabel.Text = "timestamp";
            //
            // cardAi
            //
            this.cardAi.Controls.Add(this.providerHost);
            this.cardAi.Controls.Add(this.modelHost);
            this.cardAi.Controls.Add(this.modelLabel);
            this.cardAi.Controls.Add(this.baseUrlHost);
            this.cardAi.Controls.Add(this.baseUrlLabel);
            this.cardAi.Controls.Add(this.keyHost);
            this.cardAi.Controls.Add(this.apiKeyLabel);
            this.cardAi.Controls.Add(this.aiNote);
            this.cardAi.Location = new Point(24, 436);
            this.cardAi.Name = "cardAi";
            this.cardAi.Size = new Size(592, 188);
            this.cardAi.TabIndex = 3;
            this.cardAi.Title = "AI grammar";
            this.cardAi.Description = "Used by the Grammar button";
            //
            // providerHost
            //
            this.providerHost.Controls.Add(this.providerBox);
            this.providerHost.Location = new Point(16, 54);
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
            // modelHost
            //
            this.modelHost.Controls.Add(this.modelBox);
            this.modelHost.Location = new Point(186, 54);
            this.modelHost.Name = "modelHost";
            this.modelHost.Size = new Size(220, 32);
            this.modelHost.TabIndex = 1;
            //
            // modelBox
            //
            this.modelBox.Location = new Point(10, 8);
            this.modelBox.Name = "modelBox";
            this.modelBox.Size = new Size(200, 16);
            this.modelBox.TabIndex = 0;
            //
            // modelLabel
            //
            this.modelLabel.AutoSize = true;
            this.modelLabel.Location = new Point(416, 63);
            this.modelLabel.Name = "modelLabel";
            this.modelLabel.Text = "model";
            //
            // baseUrlHost
            //
            this.baseUrlHost.Controls.Add(this.baseUrlBox);
            this.baseUrlHost.Location = new Point(16, 92);
            this.baseUrlHost.Name = "baseUrlHost";
            this.baseUrlHost.Size = new Size(390, 32);
            this.baseUrlHost.TabIndex = 2;
            //
            // baseUrlBox
            //
            this.baseUrlBox.Location = new Point(10, 8);
            this.baseUrlBox.Name = "baseUrlBox";
            this.baseUrlBox.Size = new Size(370, 16);
            this.baseUrlBox.TabIndex = 0;
            //
            // baseUrlLabel
            //
            this.baseUrlLabel.AutoSize = true;
            this.baseUrlLabel.Location = new Point(416, 101);
            this.baseUrlLabel.Name = "baseUrlLabel";
            this.baseUrlLabel.Text = "base URL (MiMo)";
            //
            // keyHost
            //
            this.keyHost.Controls.Add(this.apiKeyBox);
            this.keyHost.Location = new Point(16, 130);
            this.keyHost.Name = "keyHost";
            this.keyHost.Size = new Size(390, 32);
            this.keyHost.TabIndex = 3;
            //
            // apiKeyBox
            //
            this.apiKeyBox.Location = new Point(10, 8);
            this.apiKeyBox.Name = "apiKeyBox";
            this.apiKeyBox.Size = new Size(370, 16);
            this.apiKeyBox.TabIndex = 0;
            this.apiKeyBox.UseSystemPasswordChar = true;
            //
            // apiKeyLabel
            //
            this.apiKeyLabel.AutoSize = true;
            this.apiKeyLabel.Location = new Point(416, 139);
            this.apiKeyLabel.Name = "apiKeyLabel";
            this.apiKeyLabel.Text = "API key";
            //
            // aiNote
            //
            this.aiNote.Location = new Point(16, 164);
            this.aiNote.Name = "aiNote";
            this.aiNote.Size = new Size(560, 18);
            this.aiNote.Text = "Keys: mimo.mi.com Token Plan (MiMo)  ·  aistudio.google.com (Gemini)  ·  platform.openai.com (ChatGPT)";
            //
            // cancelButton
            //
            this.cancelButton.DialogResult = DialogResult.Cancel;
            this.cancelButton.Location = new Point(436, 636);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new Size(88, 36);
            this.cancelButton.TabIndex = 5;
            this.cancelButton.Text = "Cancel";
            //
            // saveButton
            //
            this.saveButton.Accent = true;
            this.saveButton.DialogResult = DialogResult.OK;
            this.saveButton.Location = new Point(536, 636);
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
            this.ClientSize = new Size(640, 684);
            this.Controls.Add(this.cardNote);
            this.Controls.Add(this.cardDates);
            this.Controls.Add(this.cardAi);
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
        private FieldHost providerHost;
        private ComboBox providerBox;
        private FieldHost modelHost;
        private TextBox modelBox;
        private Label modelLabel;
        private FieldHost baseUrlHost;
        private TextBox baseUrlBox;
        private Label baseUrlLabel;
        private FieldHost keyHost;
        private TextBox apiKeyBox;
        private Label apiKeyLabel;
        private Label aiNote;

        private ModernButton cancelButton;
        private ModernButton saveButton;
    }
}
