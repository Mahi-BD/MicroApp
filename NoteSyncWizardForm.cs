using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// Sets note sync up in a few pages rather than a row of boxes. The first PC makes
    /// the Firebase project (the one part nobody can do for the user) and gets a sync
    /// code back; every PC after that pastes the code and is done - no address, no
    /// password, nothing to invent or remember.
    /// </summary>
    public class NoteSyncWizardForm : PixelPerfectForm
    {
        private const string ConsoleUrl = "https://console.firebase.google.com";

        private const string FirstSteps =
            "The notes live in a Firebase project you own. It is free - Spark plan, no card - and the\r\n" +
            "notes go straight from this PC to your project, never through anyone else's.\r\n" +
            "\r\n" +
            "1.  Open the console below and create a project. Any name. Analytics and Gemini off.\r\n" +
            "\r\n" +
            "2.  Build, Firestore Database, Create database.\r\n" +
            "    Standard edition, Native mode, a region near you, production mode.\r\n" +
            "\r\n" +
            "3.  Rules tab of that database: replace everything with the rules from Copy rules,\r\n" +
            "    then press Publish. Skip this and the database refuses every read and write.\r\n" +
            "\r\n" +
            "4.  Build, Authentication, Get started, Email/Password, Enable, Save.\r\n" +
            "    (MicroApp signs itself in - you never see an address or a password.)\r\n" +
            "\r\n" +
            "5.  Gear icon, Project settings, General, Your apps, Web app (the </> button).\r\n" +
            "    The snippet it shows holds projectId and apiKey. Put them below.\r\n";

        private readonly Panel _pageChoose = new Panel();
        private readonly Panel _pageFirst = new Panel();
        private readonly Panel _pageJoin = new Panel();
        private readonly Panel _pageDone = new Panel();

        private ModernRadioButton _localOnly;
        private ModernRadioButton _makeNew;
        private ModernRadioButton _joinExisting;
        private TextBox _projectBox;
        private TextBox _keyBox;
        private TextBox _codeInBox;
        private TextBox _codeOutBox;
        private Label _doneHeading;
        private Label _doneBody;
        private Label _errorLabel;
        private TextBox _stepsBox;

        private ModernButton _back;
        private ModernButton _next;
        private ModernButton _cancel;

        private Panel _current;

        public static void Run(IWin32Window owner)
        {
            using (var form = new NoteSyncWizardForm())
            {
                form.ShowDialog(owner);
            }
        }

        private NoteSyncWizardForm()
        {
            bool dark = ThemeHelper.IsDarkMode;
            Theme.Init(dark);

            Text = "Set up note sync";
            Font = new Font("Segoe UI", 9F);
            BackColor = Theme.Bg;
            ClientSize = new Size(640, 520);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            try { Icon = Properties.Resources.AppIcon; } catch (Exception) { }

            BuildChoose();
            BuildFirst();
            BuildJoin();
            BuildDone();

            foreach (var page in new[] { _pageChoose, _pageFirst, _pageJoin, _pageDone })
            {
                page.Location = new Point(0, 0);
                page.Size = new Size(640, 440);
                page.BackColor = Theme.Bg;
                page.Visible = false;
                Controls.Add(page);
            }

            // its own row: a transparent Label overlapping the buttons would repaint the
            // form background over their tops
            _errorLabel = new Label
            {
                AutoSize = false,
                Location = new Point(24, 444),
                Size = new Size(592, 20),
                ForeColor = Theme.Danger,
                Font = Theme.Base,
                BackColor = Color.Transparent,
                Text = ""
            };

            _back = new ModernButton { Location = new Point(360, 470), Size = new Size(80, 36), Text = "Back" };
            _back.Click += (s, e) => Show(_pageChoose);

            _cancel = new ModernButton
            {
                DialogResult = DialogResult.Cancel,
                Location = new Point(24, 470),
                Size = new Size(88, 36),
                Text = "Cancel"
            };

            _next = new ModernButton
            {
                Accent = true,
                Location = new Point(452, 470),
                Size = new Size(164, 36),
                Text = "Next"
            };
            _next.Click += Next_Click;

            Controls.Add(_errorLabel);
            Controls.Add(_back);
            Controls.Add(_cancel);
            Controls.Add(_next);

            AcceptButton = _next;
            CancelButton = _cancel;

            Theme.Apply(this);
            _errorLabel.ForeColor = Theme.Danger;
            _stepsBox.BackColor = Theme.Bg;        // reads as prose, not as a field to fill in

            Show(NoteCloud.IsOn ? _pageDone : _pageChoose);
            if (NoteCloud.IsOn) ShowConnected();

            Load += (s, e) =>
            {
                Native.SetDarkModeForWindow(Handle, dark);
                Theme.RoundWindowCorners(Handle);
            };
        }

        // ---------------------------------------------------------------- pages

        private Label Heading(string text)
        {
            return new Label
            {
                AutoSize = true,
                Location = new Point(24, 24),
                Font = Theme.Heading,
                ForeColor = Theme.Text,
                BackColor = Color.Transparent,
                Text = text
            };
        }

        private Label Caption(string text, int x, int y)
        {
            return new Label
            {
                AutoSize = true,
                Location = new Point(x, y),
                Font = new Font(Theme.Small, FontStyle.Bold),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Text = text
            };
        }

        private Label Body(string text, int y, int height)
        {
            return new Label
            {
                AutoSize = false,
                Location = new Point(24, y),
                Size = new Size(592, height),
                Font = Theme.Base,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Text = text
            };
        }

        private void BuildChoose()
        {
            _localOnly = new ModernRadioButton
            {
                Location = new Point(24, 126),
                Size = new Size(592, 28),
                Text = "Just this PC - leave the notes as plain files here"
            };
            _makeNew = new ModernRadioButton
            {
                Location = new Point(24, 196),
                Size = new Size(592, 28),
                Checked = true,
                Text = "This is my first PC - set up a new place for the notes"
            };
            _joinExisting = new ModernRadioButton
            {
                Location = new Point(24, 282),
                Size = new Size(592, 28),
                Text = "Another PC is already set up - I have a sync code"
            };

            _pageChoose.Controls.Add(Heading("Keep these notes on every PC?"));
            _pageChoose.Controls.Add(Body(
                "Notes are ordinary .txt files in a folder on this PC, and that is all they ever have to " +
                "be. Sync is optional: it adds a copy in a database you own, so the same notes turn up " +
                "everywhere you use MicroApp.", 62, 52));
            _pageChoose.Controls.Add(_localOnly);
            _pageChoose.Controls.Add(Body(
                "Nothing to set up and nothing leaves this PC. This is how MicroApp works out of the box.",
                158, 20));
            _pageChoose.Controls.Add(_makeNew);
            _pageChoose.Controls.Add(Body(
                "Takes a few minutes: you make a free Firebase project, and MicroApp hands you a sync " +
                "code for the other PCs.", 228, 36));
            _pageChoose.Controls.Add(_joinExisting);
            _pageChoose.Controls.Add(Body(
                "Takes a few seconds: paste the code and this PC joins the same notes.", 314, 20));
        }

        private void BuildFirst()
        {
            _stepsBox = new TextBox
            {
                Location = new Point(24, 62),
                Size = new Size(592, 250),
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Bg,
                ForeColor = Theme.Text,
                Font = new Font("Segoe UI", 9F),
                Text = FirstSteps
            };
            _stepsBox.GotFocus += (s, e) => _stepsBox.Select(0, 0);

            var open = new ModernButton
            {
                Location = new Point(24, 320),
                Size = new Size(170, 34),
                Text = "Open Firebase console"
            };
            open.Click += (s, e) =>
            {
                try { Process.Start(ConsoleUrl); }
                catch (Exception) { ModernDialog.Info("Could not open the browser", ConsoleUrl); }
            };

            var copy = new ModernButton
            {
                Location = new Point(206, 320),
                Size = new Size(110, 34),
                Text = "Copy rules"
            };
            copy.Click += (s, e) =>
            {
                try { Clipboard.SetText(NoteCloud.Rules); copy.Text = "Copied"; }
                catch (Exception) { }
            };

            var projectHost = new FieldHost { Location = new Point(24, 388), Size = new Size(230, 32) };
            _projectBox = new TextBox { Location = new Point(10, 8), Size = new Size(210, 16) };
            projectHost.Controls.Add(_projectBox);

            var keyHost = new FieldHost { Location = new Point(264, 388), Size = new Size(352, 32) };
            _keyBox = new TextBox { Location = new Point(10, 8), Size = new Size(332, 16) };
            keyHost.Controls.Add(_keyBox);

            _pageFirst.Controls.Add(Heading("Your own Firebase project"));
            _pageFirst.Controls.Add(_stepsBox);
            _pageFirst.Controls.Add(open);
            _pageFirst.Controls.Add(copy);
            _pageFirst.Controls.Add(Caption("PROJECT ID", 24, 370));
            _pageFirst.Controls.Add(Caption("WEB API KEY", 264, 370));
            _pageFirst.Controls.Add(projectHost);
            _pageFirst.Controls.Add(keyHost);
        }

        private void BuildJoin()
        {
            var host = new FieldHost { Location = new Point(24, 190), Size = new Size(592, 96) };
            _codeInBox = new TextBox
            {
                Location = new Point(10, 8),
                Size = new Size(572, 76),
                Multiline = true,
                Font = new Font("Consolas", 9F)
            };
            host.Controls.Add(_codeInBox);

            _pageJoin.Controls.Add(Heading("Paste the sync code"));
            _pageJoin.Controls.Add(Body(
                "On the PC that is already syncing, open Note Setting and press Sync code. Copy what it " +
                "shows and paste it here. The code carries everything this PC needs - there is nothing " +
                "else to fill in.", 62, 64));
            _pageJoin.Controls.Add(Caption("SYNC CODE", 24, 168));
            _pageJoin.Controls.Add(host);
        }

        private void BuildDone()
        {
            _doneHeading = Heading("Sync is on");
            _doneBody = Body("", 62, 64);

            var host = new FieldHost { Location = new Point(24, 190), Size = new Size(592, 96) };
            _codeOutBox = new TextBox
            {
                Location = new Point(10, 8),
                Size = new Size(572, 76),
                Multiline = true,
                ReadOnly = true,
                Font = new Font("Consolas", 9F)
            };
            host.Controls.Add(_codeOutBox);

            var copy = new ModernButton
            {
                Location = new Point(24, 300),
                Size = new Size(130, 34),
                Text = "Copy sync code"
            };
            copy.Click += (s, e) =>
            {
                try { Clipboard.SetText(_codeOutBox.Text); copy.Text = "Copied"; }
                catch (Exception) { }
            };

            _pageDone.Controls.Add(_doneHeading);
            _pageDone.Controls.Add(_doneBody);
            _pageDone.Controls.Add(Caption("SYNC CODE FOR YOUR OTHER PCs", 24, 168));
            _pageDone.Controls.Add(host);
            _pageDone.Controls.Add(copy);
            _pageDone.Controls.Add(Body(
                "Anyone holding this code can read these notes, so pass it across the way you would a " +
                "password. It is stored on this PC, and Note Setting can show it again later.", 344, 48));
        }

        // ---------------------------------------------------------------- flow

        private void Show(Panel page)
        {
            if (_current != null) _current.Visible = false;
            _current = page;
            page.Visible = true;
            page.BringToFront();

            if (_back != null)
            {
                _back.Visible = page == _pageFirst || page == _pageJoin;
                _next.Text = page == _pageChoose ? "Next"
                           : page == _pageFirst ? "Create and connect"
                           : page == _pageJoin ? "Join"
                           : "Close";
                _cancel.Visible = page != _pageDone;
                _errorLabel.Text = "";
            }
        }

        private void ShowConnected()
        {
            _doneHeading.Text = "Sync is on";
            _doneBody.Text = NoteCloud.Status.Length > 0
                ? NoteCloud.Status
                : "These notes are being kept in your Firebase project.";
            _codeOutBox.Text = NoteCloud.Code();
        }

        private void Next_Click(object sender, EventArgs e)
        {
            if (_current == _pageChoose)
            {
                if (_localOnly.Checked)
                {
                    if (NoteCloud.IsOn && !ModernDialog.Confirm("Stop syncing",
                        "The notes already on this PC stay put, and the ones in your database stay there too.",
                        "Stop syncing", "Keep syncing")) return;
                    if (NoteCloud.IsOn) NoteCloud.Disconnect();
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }
                Show(_makeNew.Checked ? _pageFirst : _pageJoin);
                return;
            }

            if (_current == _pageDone)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            _next.Enabled = false;
            _errorLabel.Text = "";
            Cursor = Cursors.WaitCursor;
            try
            {
                string failed = _current == _pageFirst
                    ? NoteCloud.StartFresh(_projectBox.Text, _keyBox.Text)
                    : NoteCloud.Join(_codeInBox.Text);
                if (failed != null)
                {
                    _errorLabel.Text = failed;
                    return;
                }

                string trouble = NoteCloud.SyncNow();
                Show(_pageDone);
                ShowConnected();
                if (trouble != null)
                {
                    _doneHeading.Text = "Signed in, but the first sync failed";
                    _doneBody.Text = trouble;
                }
            }
            finally
            {
                Cursor = Cursors.Default;
                _next.Enabled = true;
            }
        }
    }
}
