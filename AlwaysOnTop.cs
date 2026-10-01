using System;
using System.Collections.Generic;
using System.Drawing;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// Always on Top: pins any window above the others. Pinning can also fade the window
    /// (AOT Setting > opacity) and mark its title; unpinning puts all of that back exactly
    /// as it was. MicroApp keeps a record of what it changed so it can undo it, and undoes
    /// everything when it exits.
    /// </summary>
    static class AlwaysOnTop
    {
        public const string TitleMark = "\U0001F4CC ";   // 📌

        class Pin
        {
            public bool HadLayered;
            public bool HadAlpha;
            public byte Alpha = 255;
            public string MarkedTitle;   // what we set the title to (null = not marked)
        }

        static readonly Dictionary<IntPtr, Pin> _pins = new Dictionary<IntPtr, Pin>();

        public static int Count
        {
            get { Prune(); return _pins.Count; }
        }

        /// <summary>The top-level window under a screen point, or zero for the desktop and the taskbar.</summary>
        public static IntPtr WindowAt(Point screen)
        {
            IntPtr h = WindowFromPoint(new POINT { X = screen.X, Y = screen.Y });
            if (h == IntPtr.Zero) return IntPtr.Zero;
            h = GetAncestor(h, 2 /* GA_ROOT */);
            if (h == IntPtr.Zero || IsShellWindow(h)) return IntPtr.Zero;
            return h;
        }

        /// <summary>The window's visible frame (without the invisible resize borders of Windows 10).</summary>
        public static Rectangle Bounds(IntPtr h)
        {
            RECT r;
            if (DwmGetWindowAttribute(h, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out r, Marshal.SizeOf(typeof(RECT))) == 0)
                return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            if (GetWindowRect(h, out r)) return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            return Rectangle.Empty;
        }

        public static bool IsTopmost(IntPtr h)
        {
            return (GetWindowLong(h, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
        }

        /// <summary>
        /// Pins an unpinned window, or unpins a pinned one (when AOT Setting allows a second
        /// click to unpin). Tells the user what happened.
        /// </summary>
        public static void Toggle(IntPtr h)
        {
            if (h == IntPtr.Zero || !IsWindow(h)) { Feedback(false, "There is no window there to pin."); return; }
            string name = Title(h);
            if (IsTopmost(h))
            {
                if (!Properties.Settings.Default.AotClickUnpins)
                {
                    Feedback(true, "Already on top: " + name);
                    return;
                }
                Unpin(h);
                Feedback(true, "No longer on top: " + name);
                return;
            }

            string error;
            if (!PinWindow(h, out error)) { Feedback(false, error); return; }
            Feedback(true, "Always on top: " + name);
        }

        static bool PinWindow(IntPtr h, out string error)
        {
            error = null;
            SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            if (!IsTopmost(h))
            {
                // Windows will not let a normal app change a window of an app running as administrator
                error = "Windows did not allow it. The window probably belongs to an app running as administrator - " +
                        "run MicroApp as administrator to pin it.";
                return false;
            }

            var pin = new Pin();
            int ex = GetWindowLong(h, GWL_EXSTYLE);
            pin.HadLayered = (ex & WS_EX_LAYERED) != 0;
            if (pin.HadLayered)
            {
                uint key; byte alpha; uint flags;
                if (GetLayeredWindowAttributes(h, out key, out alpha, out flags) && (flags & LWA_ALPHA) != 0)
                {
                    pin.HadAlpha = true;
                    pin.Alpha = alpha;
                }
            }
            _pins[h] = pin;
            Watch();
            ApplyLook(h, pin);
            return true;
        }

        /// <summary>Takes a window off the top and gives back its own opacity and title.</summary>
        public static void Unpin(IntPtr h)
        {
            SetWindowPos(h, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            Pin pin;
            if (!_pins.TryGetValue(h, out pin)) return;
            _pins.Remove(h);
            if (_pins.Count == 0) StopWatch();
            if (!IsWindow(h)) return;
            RestoreOpacity(h, pin);
            RestoreTitle(h, pin);
        }

        public static void UnpinAll()
        {
            foreach (IntPtr h in new List<IntPtr>(_pins.Keys)) Unpin(h);
        }

        /// <summary>Re-applies opacity and the title mark to every pinned window after AOT Setting is saved.</summary>
        public static void RefreshAll()
        {
            Prune();
            foreach (var kv in new List<KeyValuePair<IntPtr, Pin>>(_pins)) ApplyLook(kv.Key, kv.Value);
        }

        static void ApplyLook(IntPtr h, Pin pin)
        {
            int percent = Math.Max(20, Math.Min(100, Properties.Settings.Default.AotOpacity));
            if (percent < 100)
            {
                int ex = GetWindowLong(h, GWL_EXSTYLE);
                if ((ex & WS_EX_LAYERED) == 0) SetWindowLong(h, GWL_EXSTYLE, ex | WS_EX_LAYERED);
                SetLayeredWindowAttributes(h, 0, (byte)Math.Round(percent * 2.55), LWA_ALPHA);
            }
            else
            {
                RestoreOpacity(h, pin);
            }

            if (Properties.Settings.Default.AotTitleMark)
            {
                string title = Title(h);
                if (!title.StartsWith(TitleMark))
                {
                    pin.MarkedTitle = TitleMark + title;
                    SetWindowText(h, pin.MarkedTitle);
                }
            }
            else
            {
                RestoreTitle(h, pin);
            }
        }

        static void RestoreOpacity(IntPtr h, Pin pin)
        {
            if (pin.HadLayered)
            {
                if (pin.HadAlpha) SetLayeredWindowAttributes(h, 0, pin.Alpha, LWA_ALPHA);
                else SetLayeredWindowAttributes(h, 0, 255, LWA_ALPHA);
            }
            else
            {
                int ex = GetWindowLong(h, GWL_EXSTYLE);
                if ((ex & WS_EX_LAYERED) != 0)
                {
                    SetLayeredWindowAttributes(h, 0, 255, LWA_ALPHA);
                    SetWindowLong(h, GWL_EXSTYLE, ex & ~WS_EX_LAYERED);
                }
            }
        }

        static void RestoreTitle(IntPtr h, Pin pin)
        {
            if (pin.MarkedTitle == null) return;
            string title = Title(h);
            // only take the mark off if the app has not retitled the window itself meanwhile
            if (title.StartsWith(TitleMark)) SetWindowText(h, title.Substring(TitleMark.Length));
            pin.MarkedTitle = null;
        }

        // ---------------------------------------------------------------- keeping them on top

        // Being topmost is not enough on its own: another app's topmost window -- a full-screen
        // Remote Desktop, a video player, Task Manager -- rises above every other topmost window
        // each time it is activated. So while anything is pinned, MicroApp watches: whenever the
        // foreground changes, and a few times a second besides, a pinned window that another
        // app's topmost window has covered is lifted back to the top. MicroApp's own windows
        // (the capture frame, notes, toasts) are left alone so they still come out on top.

        static System.Windows.Forms.Timer _watch;
        static WinEventProc _foregroundProc;   // kept in a field so the GC never collects the callback
        static IntPtr _foregroundHook;
        static readonly uint OwnPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        static void Watch()
        {
            if (_watch == null)
            {
                _watch = new System.Windows.Forms.Timer { Interval = 250 };
                _watch.Tick += delegate { KeepOnTop(); };
                _watch.Start();
            }
            if (_foregroundHook == IntPtr.Zero)
            {
                _foregroundProc = delegate
                {
                    KeepOnTop();
                    // a Remote Desktop window raises itself a moment after it is activated
                    var again = new System.Windows.Forms.Timer { Interval = 120 };
                    again.Tick += delegate { again.Stop(); again.Dispose(); KeepOnTop(); };
                    again.Start();
                };
                _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
                                                  _foregroundProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            }
        }

        static void StopWatch()
        {
            if (_watch != null) { _watch.Stop(); _watch.Dispose(); _watch = null; }
            if (_foregroundHook != IntPtr.Zero) { UnhookWinEvent(_foregroundHook); _foregroundHook = IntPtr.Zero; }
            _foregroundProc = null;
        }

        /// <summary>Lifts every pinned window that another app's topmost window has got on top of.</summary>
        static void KeepOnTop()
        {
            Prune();
            if (_pins.Count == 0) { StopWatch(); return; }
            foreach (IntPtr h in new List<IntPtr>(_pins.Keys))
            {
                if (!IsWindowVisible(h) || IsIconic(h)) continue;
                if (CoveredByOtherTopmost(h))
                    SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            }
        }

        /// <summary>
        /// True when a visible topmost window of another app sits above <paramref name="h"/> and
        /// overlaps it. Menus, tooltips and other small pop-ups do not count -- lifting the pinned
        /// window over them would hide the menu the user just opened.
        /// </summary>
        static bool CoveredByOtherTopmost(IntPtr h)
        {
            Rectangle mine = Bounds(h);
            if (mine.IsEmpty) return false;
            int guard = 0;
            for (IntPtr w = GetWindow(h, GW_HWNDPREV); w != IntPtr.Zero && guard < 2000; w = GetWindow(w, GW_HWNDPREV), guard++)
            {
                if (!IsWindowVisible(w) || _pins.ContainsKey(w)) continue;
                int ex = GetWindowLong(w, GWL_EXSTYLE);
                if ((ex & WS_EX_TOPMOST) == 0) continue;
                if ((ex & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT)) != 0) continue;
                uint pid;
                GetWindowThreadProcessId(w, out pid);
                if (pid == OwnPid) continue;                       // our own capture frame, notes, toasts
                if (IsPopupClass(w) || IsCloaked(w)) continue;
                Rectangle r = Bounds(w);
                if (r.Width < 200 || r.Height < 120) continue;   // small pop-ups and badges
                if (r.IntersectsWith(mine)) return true;
            }
            return false;
        }

        static bool IsPopupClass(IntPtr w)
        {
            var sb = new StringBuilder(64);
            GetClassName(w, sb, sb.Capacity);
            string c = sb.ToString();
            return c == "#32768" || c == "tooltips_class32" || c == "Xaml_WindowedPopupClass" || c == "SysShadow" ||
                   c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd" || c == "NotifyIconOverflowWindow" ||
                   c == "Windows.UI.Core.CoreWindow" || c == "TopLevelWindowForOverflowXamlIsland";
        }

        static bool IsCloaked(IntPtr w)
        {
            int cloaked;
            return DwmGetWindowAttribute(w, 14 /* DWMWA_CLOAKED */, out cloaked, 4) == 0 && cloaked != 0;
        }

        static void Prune()
        {
            foreach (IntPtr h in new List<IntPtr>(_pins.Keys))
                if (!IsWindow(h)) _pins.Remove(h);
        }

        static void Feedback(bool ok, string text)
        {
            if (!ok)
            {
                SystemSounds.Beep.Play();
                Toast.Show(text);
                return;
            }
            if (Properties.Settings.Default.AotSound) SystemSounds.Asterisk.Play();
            if (Properties.Settings.Default.AotNotify) Toast.Show(text);
        }

        static string Title(IntPtr h)
        {
            int len = GetWindowTextLength(h);
            var sb = new StringBuilder(Math.Max(1, len + 1));
            GetWindowText(h, sb, sb.Capacity);
            string t = sb.ToString();
            return t.Length == 0 ? "(untitled window)" : t;
        }

        static bool IsShellWindow(IntPtr h)
        {
            var sb = new StringBuilder(64);
            GetClassName(h, sb, sb.Capacity);
            string c = sb.ToString();
            return c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd" || c == "Progman" || c == "WorkerW";
        }

        // ---------------------------------------------------------------- Win32

        const int GWL_EXSTYLE = -20;
        const int WS_EX_TOPMOST = 0x8;
        const int WS_EX_LAYERED = 0x80000;
        const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TRANSPARENT = 0x20;
        const uint GW_HWNDPREV = 3;
        const uint SWP_NOOWNERZORDER = 0x200;
        const uint EVENT_SYSTEM_FOREGROUND = 3;
        const uint WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;
        const uint LWA_ALPHA = 0x2;
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int index, int value);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] static extern bool GetLayeredWindowAttributes(IntPtr h, out uint key, out byte alpha, out uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetWindowText(IntPtr h, string s);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    }

    /// <summary>
    /// Tray → AOT Setting: the hot key, how faded a pinned window is, and how pinning behaves.
    /// </summary>
    class AotSettingsForm : PixelPerfectForm
    {
        static readonly string[] ModNames = { "Alt", "Ctrl", "Shift", "Win" };
        static readonly int[] ModBits = { 1, 2, 4, 8 };

        readonly ModernCheckBox[] _mods = new ModernCheckBox[4];
        readonly TextBox _key;
        readonly ModernSlider _opacity;
        readonly Label _opacityValue;
        readonly ModernCheckBox _clickUnpins, _titleMark, _notify, _sound;
        readonly Label _pinnedLabel;
        readonly ModernButton _unpinAll;

        public AotSettingsForm()
        {
            Theme.Init(ThemeHelper.IsDarkMode);

            Text = "MicroApp - Always on Top";
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
            Controls.Add(iconBox);
            Controls.Add(new Label
            {
                Location = new Point(84, 24),
                AutoSize = true,
                Text = "Always on Top",
                Font = new Font("Segoe UI Semibold", 12.5F),
                ForeColor = Theme.Text,
                BackColor = Color.Transparent
            });
            Controls.Add(new Label
            {
                Location = new Point(86, 52),
                Size = new Size(430, 32),
                Text = "Press the hot key (or tray → Always on Top), then click any window to keep it above " +
                       "the others. Click a pinned window the same way to let it go.",
                Font = Theme.Small,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent
            });

            // ----- hot key --------------------------------------------------------------
            var keyCard = new Card
            {
                Location = new Point(24, 96),
                Size = new Size(500, 110),
                Title = "Hot key",
                Description = "Tick the modifiers and give it a key. Clear the key box to turn it off."
            };
            Controls.Add(keyCard);
            int mods = Properties.Settings.Default.AotHotKeyModifier;
            for (int m = 0; m < 4; m++)
            {
                _mods[m] = new ModernCheckBox
                {
                    Location = new Point(20 + m * 78, 66),
                    AutoSize = false,
                    Size = new Size(74, 24),
                    Text = ModNames[m],
                    Checked = (mods & ModBits[m]) != 0
                };
                keyCard.Controls.Add(_mods[m]);
            }
            keyCard.Controls.Add(new Label { Location = new Point(338, 68), AutoSize = true, Text = "Key", Font = Theme.Base, ForeColor = Theme.TextDim, BackColor = Color.Transparent });
            // same field as the other setting windows: press a key to set it, Delete clears it
            var keyHost = new FieldHost { Location = new Point(374, 60), Size = new Size(96, 32) };
            _key = new TextBox
            {
                Location = new Point(10, 8),
                Size = new Size(76, 16),
                MaxLength = 12,
                Text = Properties.Settings.Default.AotHotKey,
                TextAlign = HorizontalAlignment.Center,
                BackColor = Theme.FieldBg,
                ForeColor = Theme.Text
            };
            _key.KeyDown += Key_KeyDown;
            keyHost.Controls.Add(_key);
            keyCard.Controls.Add(keyHost);

            // ----- pinned windows ---------------------------------------------------------
            var look = new Card
            {
                Location = new Point(24, keyCard.Bottom + 12),
                Size = new Size(500, 236),
                Title = "Pinned windows",
                Description = "How a window looks and behaves while it is kept on top."
            };
            Controls.Add(look);
            look.Controls.Add(new Label { Location = new Point(20, 66), AutoSize = true, Text = "Opacity", Font = Theme.Base, ForeColor = Theme.Text, BackColor = Color.Transparent });
            _opacity = new ModernSlider
            {
                Location = new Point(110, 62),
                Size = new Size(300, 26),
                Minimum = 20,
                Maximum = 100,
                Value = Math.Max(20, Math.Min(100, Properties.Settings.Default.AotOpacity))
            };
            _opacityValue = new Label { Location = new Point(420, 66), Size = new Size(60, 20), Font = Theme.Base, ForeColor = Theme.Text, BackColor = Color.Transparent };
            _opacity.ValueChanged += delegate { _opacityValue.Text = _opacity.Value + " %"; };
            _opacityValue.Text = _opacity.Value + " %";
            look.Controls.Add(_opacity);
            look.Controls.Add(_opacityValue);

            _clickUnpins = Check(look, 104, "Clicking a pinned window again takes it off the top", Properties.Settings.Default.AotClickUnpins);
            _titleMark = Check(look, 134, "Put a \U0001F4CC pin in the title of a pinned window", Properties.Settings.Default.AotTitleMark);
            _notify = Check(look, 164, "Show a notification when a window is pinned or let go", Properties.Settings.Default.AotNotify);
            _sound = Check(look, 194, "Play a sound when a window is pinned or let go", Properties.Settings.Default.AotSound);

            // ----- currently pinned ------------------------------------------------------------
            var now = new Card
            {
                Location = new Point(24, look.Bottom + 12),
                Size = new Size(500, 104),
                Title = "Right now",
                Description = "Windows are let go when MicroApp exits."
            };
            Controls.Add(now);
            _pinnedLabel = new Label { Location = new Point(20, 68), Size = new Size(300, 20), Font = Theme.Base, ForeColor = Theme.Text, BackColor = Color.Transparent };
            _unpinAll = new ModernButton { Text = "Unpin all", Size = new Size(120, 32), Location = new Point(500 - 20 - 120, 60) };
            _unpinAll.Click += delegate { AlwaysOnTop.UnpinAll(); UpdatePinned(); };
            now.Controls.Add(_pinnedLabel);
            now.Controls.Add(_unpinAll);
            UpdatePinned();

            // ----- buttons ------------------------------------------------------------------
            var save = new ModernButton
            {
                Text = "Save",
                Accent = true,
                Size = new Size(112, 36),
                Location = new Point(500 + 24 - 112, now.Bottom + 14)
            };
            save.Click += Save_Click;
            var cancel = new ModernButton
            {
                Text = "Cancel",
                Size = new Size(96, 36),
                Location = new Point(save.Left - 96 - 8, now.Bottom + 14),
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(save);
            Controls.Add(cancel);
            AcceptButton = save;
            CancelButton = cancel;

            ClientSize = new Size(500 + 48, save.Bottom + 20);
            ResumeLayout();

            Theme.Apply(this);
            Native.SetDarkModeForWindow(Handle, ThemeHelper.IsDarkMode);
            Theme.RoundWindowCorners(Handle);
            Icon = Properties.Resources.AppIcon;
        }

        static ModernCheckBox Check(Card card, int y, string text, bool value)
        {
            var c = new ModernCheckBox { Location = new Point(18, y), AutoSize = false, Size = new Size(card.Width - 36, 26), Text = text, Checked = value };
            card.Controls.Add(c);
            return c;
        }

        /// <summary>Shows the pressed key in the box, ignoring bare modifiers (as Capture Setting does).</summary>
        void Key_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Menu: case Keys.LMenu: case Keys.RMenu:
                case Keys.ShiftKey: case Keys.LShiftKey: case Keys.RShiftKey:
                case Keys.ControlKey: case Keys.LControlKey: case Keys.RControlKey:
                case Keys.LWin: case Keys.RWin: case Keys.Return: case Keys.Tab: case Keys.Escape:
                    return;
                case Keys.Delete:
                case Keys.Back:
                    _key.Text = string.Empty;
                    break;
                default:
                    _key.Text = e.KeyCode.ToString();
                    break;
            }
            e.SuppressKeyPress = true;
        }

        void UpdatePinned()
        {
            int n = AlwaysOnTop.Count;
            _pinnedLabel.Text = n == 0 ? "No window is pinned." : n == 1 ? "1 window is pinned." : n + " windows are pinned.";
            _unpinAll.Enabled = n > 0;
        }

        void Save_Click(object sender, EventArgs e)
        {
            string letter = _key.Text.Trim();
            if (letter.Length == 1) letter = letter.ToUpperInvariant();
            if (letter.Length > 0)
            {
                Keys parsed;
                if (!Enum.TryParse(letter, true, out parsed))
                {
                    ModernDialog.Info("That is not a key", "Use a letter, a digit or a key name such as F8.");
                    return;
                }
            }
            int mods = 0;
            for (int m = 0; m < 4; m++) if (_mods[m].Checked) mods |= ModBits[m];

            Properties.Settings.Default.AotHotKey = letter;
            Properties.Settings.Default.AotHotKeyModifier = mods;
            Properties.Settings.Default.AotOpacity = _opacity.Value;
            Properties.Settings.Default.AotClickUnpins = _clickUnpins.Checked;
            Properties.Settings.Default.AotTitleMark = _titleMark.Checked;
            Properties.Settings.Default.AotNotify = _notify.Checked;
            Properties.Settings.Default.AotSound = _sound.Checked;
            Properties.Settings.Default.Save();
            AlwaysOnTop.RefreshAll();
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
