using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// Notes put out of the way. Archiving takes a note out of the main list and drops it
    /// here, newest first, where it can be searched, read, put back or deleted. The file
    /// itself never moves - "archived" is one flag in the .notes-meta sidecar.
    /// </summary>
    public class NoteArchiveForm : PixelPerfectForm
    {
        private static NoteArchiveForm _open;

        private readonly NoteListView _list;
        private readonly TextBox _searchBox;

        /// <summary>Full text per note, so typing in the search box does not re-read the folder each keystroke.</summary>
        private readonly Dictionary<string, string> _text =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void Open()
        {
            if (_open != null)
            {
                _open.Reload();
                _open.Activate();
                return;
            }
            _open = new NoteArchiveForm();
            _open.FormClosed += (s, e) => _open = null;
            _open.Show();
        }

        /// <summary>Someone archived, unarchived or synced a note while this window was open.</summary>
        public static void RefreshList()
        {
            if (_open == null) return;
            try { _open.Reload(); } catch (Exception) { }
        }

        private NoteArchiveForm()
        {
            bool dark = ThemeHelper.IsDarkMode;
            Theme.Init(dark);

            Text = "Archive";
            Font = new Font("Segoe UI", 9F);
            BackColor = Theme.Bg;
            ClientSize = new Size(560, 460);
            MinimumSize = new Size(440, 320);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            try { Icon = Properties.Resources.AppIcon; } catch (Exception) { }

            // ---- header: what this is, and the search box in the top right
            var header = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Theme.Bg };

            var heading = new Label
            {
                AutoSize = true,
                Location = new Point(12, 16),
                Font = Theme.Strong,
                ForeColor = Theme.Text,
                BackColor = Color.Transparent,
                Text = "Archive"
            };

            var searchHost = new FieldHost { Size = new Size(220, 32), Location = new Point(12, 12) };
            _searchBox = new TextBox { Location = new Point(10, 8), Size = new Size(200, 16) };
            _searchBox.TextChanged += (s, e) => Reload();
            searchHost.Controls.Add(_searchBox);

            header.Controls.Add(heading);
            header.Controls.Add(searchHost);
            header.Resize += (s, e) =>
                searchHost.Location = new Point(header.Width - searchHost.Width - 12, 12);

            // ---- the list
            _list = new NoteListView { Dock = DockStyle.Fill, AllowReorder = false };
            _list.OpenRequested += (s, e) => OpenSelected();
            _list.RowMenuRequested += (s, e) => ShowRowMenu(e.Location);

            var listHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(6) };
            listHost.Controls.Add(_list);

            // ---- footer
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Theme.Bg };
            var unarchiveButton = new ModernButton
            {
                Text = "Unarchive",
                Size = new Size(96, 32),
                Location = new Point(12, 10)
            };
            unarchiveButton.Click += (s, e) => Unarchive();

            var openButton = new ModernButton
            {
                Text = "Open",
                Size = new Size(80, 32),
                Accent = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            openButton.Click += (s, e) => OpenSelected();

            var deleteButton = new ModernButton
            {
                Text = "Delete",
                Size = new Size(80, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            deleteButton.Click += (s, e) => DeleteSelected();

            footer.Controls.Add(unarchiveButton);
            footer.Controls.Add(openButton);
            footer.Controls.Add(deleteButton);
            footer.Resize += (s, e) =>
            {
                openButton.Location = new Point(footer.Width - openButton.Width - 12, 10);
                deleteButton.Location = new Point(openButton.Left - deleteButton.Width - 8, 10);
            };

            Controls.Add(listHost);
            Controls.Add(header);
            Controls.Add(footer);

            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            Activated += (s, e) => Reload();

            Native.SetDarkModeForWindow(Handle, dark);
            Theme.RoundWindowCorners(Handle);

            Reload();
        }

        /// <summary>Archived notes, newest first, cut down to whatever is in the search box.</summary>
        private void Reload()
        {
            string selected = _list.SelectedPath;
            string query = _searchBox.Text.Trim();

            var found = new List<FileInfo>();
            try
            {
                foreach (string file in Directory.GetFiles(NoteStore.Folder, "*.txt"))
                {
                    if (!NoteMeta.IsArchived(file)) continue;
                    if (query.Length > 0 && !Matches(file, query)) continue;
                    found.Add(new FileInfo(file));
                }
            }
            catch (Exception) { }

            found.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));   // newest first

            var paths = new List<string>();
            foreach (var file in found) paths.Add(file.FullName);
            _list.SetPaths(paths, selected);
        }

        /// <summary>Search covers the note's name and everything written in it.</summary>
        private bool Matches(string path, string query)
        {
            if (Path.GetFileNameWithoutExtension(path).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            string body;
            string key = path + "|" + SafeStamp(path).Ticks;
            if (!_text.TryGetValue(key, out body))
            {
                try { body = File.ReadAllText(path); }
                catch (Exception) { body = ""; }
                _text[key] = body;
            }
            return body.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static DateTime SafeStamp(string path)
        {
            try { return File.GetLastWriteTimeUtc(path); }
            catch (Exception) { return DateTime.MinValue; }
        }

        private void ShowRowMenu(Point where)
        {
            string path = _list.SelectedPath;
            if (path == null) return;

            var menu = new ContextMenuStrip
            {
                Renderer = new ModernMenuRenderer(),
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Font = Theme.Base,
                ShowImageMargin = false
            };

            var open = new ToolStripMenuItem("Open") { Font = Theme.Strong };
            open.Click += (s, e) => OpenSelected();
            menu.Items.Add(open);

            var restore = new ToolStripMenuItem("Unarchive");
            restore.Click += (s, e) => Unarchive();
            menu.Items.Add(restore);

            menu.Items.Add(new ToolStripSeparator());

            var delete = new ToolStripMenuItem("Delete");
            delete.Click += (s, e) => DeleteSelected();
            menu.Items.Add(delete);

            menu.Show(_list, where);
        }

        private void OpenSelected()
        {
            string path = _list.SelectedPath;
            if (path != null) NoteForm.ShowExisting(path);
        }

        /// <summary>Back to the main list, where it keeps the colour and the place it had.</summary>
        private void Unarchive()
        {
            string path = _list.SelectedPath;
            if (path == null) return;
            NoteMeta.SetArchived(path, false);
            Reload();
            NoteListForm.RefreshList();
        }

        private void DeleteSelected()
        {
            string path = _list.SelectedPath;
            if (path == null) return;
            if (!ModernDialog.Confirm("Delete note",
                Path.GetFileName(path) + " will be deleted for good" +
                (NoteCloud.IsOn ? ", on this PC and every PC it syncs with." : "."), "Delete", "Keep it")) return;
            try { File.Delete(path); NoteTrash.Record(path); } catch (Exception) { }
            NoteMeta.Forget(path);
            Reload();
        }
    }
}
