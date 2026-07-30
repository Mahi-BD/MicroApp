using Gma.System.MouseKeyHook;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// Provides Windows dark/light theme detection and colors.
    /// </summary>
    public static class ThemeHelper
    {
        // Windows 11 dark mode colors
        public static readonly Color DarkBackground = Color.FromArgb(32, 32, 32);      // #202020
        public static readonly Color DarkSurface = Color.FromArgb(43, 43, 43);         // #2B2B2B
        public static readonly Color DarkBorder = Color.FromArgb(60, 60, 60);          // #3C3C3C
        public static readonly Color DarkText = Color.FromArgb(255, 255, 255);         // #FFFFFF
        public static readonly Color DarkTextSecondary = Color.FromArgb(180, 180, 180);// #B4B4B4

        public static event EventHandler ThemeChanged;

        static ThemeHelper()
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General)
            {
                ThemeChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Returns true if apps should use dark mode.
        /// </summary>
        public static bool IsDarkMode
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var value = key?.GetValue("AppsUseLightTheme")?.RegToUint();
                    return value.HasValue && value.Value == 0;
                }
            }
        }

        /// <summary>
        /// Returns true if the system tray/taskbar is dark.
        /// </summary>
        public static bool IsSystemDark
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var value = key?.GetValue("SystemUsesLightTheme")?.RegToUint();
                    return !value.HasValue || value.Value == 0;
                }
            }
        }

        /// <summary>
        /// Renderer for the tray menu; colours come from <see cref="Theme"/>.
        /// </summary>
        public static ToolStripRenderer GetMenuRenderer(bool dark)
        {
            Theme.Init(dark);
            return new ModernMenuRenderer();
        }
    }

    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // only run one of these at a time!
            //https://stackoverflow.com/questions/502303/how-do-i-programmatically-get-the-guid-of-an-application-in-net2-0/502323#502323
            string assyGuid = Assembly.GetExecutingAssembly().GetCustomAttribute<GuidAttribute>().Value.ToUpper();
            bool createdNew;
            new System.Threading.Mutex(false, assyGuid, out createdNew);
            if (!createdNew) return;

            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Enable dark mode for context menus (must be before any menus are created)
            Native.SetAppDarkMode(ThemeHelper.IsDarkMode);

            // Load the palette once, up front: every form and the tray menu read from it
            Theme.Init(ThemeHelper.IsDarkMode);

            // try to make sure we don't die leaving the cursor in "+" state
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;

            Application.Run(new TrayApplicationContext());

        }

        private static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            // reset cursors
            Native.SystemParametersInfo(0x0057, 0, null, 0);
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // reset cursors
            Native.SystemParametersInfo(0x0057, 0, null, 0);
        }
    }
    public static class Extensions
    {
        public static uint? RegToUint(this object value) => value == null ? (uint?)null : Convert.ToUInt32(value);
        public static string[] Names(this Type enumType) => Enum.GetNames(enumType);
        public static int Count(this Type enumType) => enumType.Names().Length;
        public static object Value(this Type enumType, string name) => Enum.Parse(enumType, name);
        public static string First(this string s, int count)
        {
            if (count > s.Length) return s;
            return s.Substring(0, count);
        }
    }
    public enum TypeMethod
    {
        Forms_SendKeys = 0,
        AutoIt_Send = 1,
        SendInput_ScanCode = 3  // Scan codes with ALT code fallback - works everywhere including VM consoles
    }
    public enum HotKeyMode
    {
        Target = 0,
        JustGo
    }
    public class TrayApplicationContext : ApplicationContext
    {
        NotifyIcon _notify = null;
        IKeyboardMouseEvents _hook = null;
        int? _usingHotKey;
        EventHandler<HotKeyEventArgs> _currentHotKeyHandler = null;
        CancellationTokenSource _stop = new CancellationTokenSource();

        // OCR hot key lives alongside the typing one, so both are registered at once
        int? _ocrHotKey;
        EventHandler<HotKeyEventArgs> _ocrHotKeyHandler = null;
        bool _ocrBusy = false;

        // screen capture: same overlay, but the picture is the product
        int? _captureHotKey;
        EventHandler<HotKeyEventArgs> _captureHotKeyHandler = null;

        // GIF recording
        int? _gifHotKey;
        EventHandler<HotKeyEventArgs> _gifHotKeyHandler = null;
        GifRecorder _recorder;
        RecordingIndicator _indicator;
        IKeyboardMouseEvents _recordHook;

        // video recording (MP4 with sound)
        int? _videoHotKey;
        EventHandler<HotKeyEventArgs> _videoHotKeyHandler = null;
        VideoRecorder _videoRecorder;
        VideoRecordingIndicator _videoIndicator;
        RecordingRegionFrame _videoFrame;
        IKeyboardMouseEvents _videoHook;

        // notes: every hot key press opens a fresh little notepad, saved as you type
        int? _noteHotKey;
        EventHandler<HotKeyEventArgs> _noteHotKeyHandler = null;

        // text picker: "+" crosshair, click an element, get its real text (UI Automation, no OCR)
        int? _pickHotKey;
        EventHandler<HotKeyEventArgs> _pickHotKeyHandler = null;
        IKeyboardMouseEvents _pickHook;
        TextPickerHud _pickHud;
        ElementOutline _pickOutline;
        System.Windows.Forms.Timer _pickTimer;
        bool _pickPeekBusy;
        IntPtr _pickTarget;

        // hot keys are raised on HotKeyManager's own message loop; this marshals the
        // UI work (overlay, dialogs, clipboard) back onto the tray thread
        Control _sync;

        bool _settingsOpen = false;
        public TrayApplicationContext()
        {
            _sync = new Control();
            _sync.CreateControl();

            StartHotKey();
            StartOcrHotKey();
            StartCaptureHotKey();
            StartGifHotKey();
            StartVideoHotKey();
            StartTextPickHotKey();
            StartNoteHotKey();
            NoteForm.OpenSettings = () => NoteSettings(this, EventArgs.Empty);
            bool darkTray = true;
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                var light = key?.GetValue("SystemUsesLightTheme")?.RegToUint();
                if (light.HasValue)
                {
                    darkTray = light.Value != 1;
                }
            }
            var traySize = SystemInformation.SmallIconSize;

            _notify = new NotifyIcon
            {
                Icon = new System.Drawing.Icon(darkTray ? Properties.Resources.Target : Properties.Resources.TargetDark, traySize.Width, traySize.Height),
                Visible = true,
                ContextMenuStrip = BuildTrayMenu(),
                Text = "MicroApp: Click to choose a target"
            };
            _notify.MouseDown += _notify_MouseDown;
        }

        /// <summary>Tray menu, themed to match the rest of the app.</summary>
        ContextMenuStrip BuildTrayMenu()
        {
            var menu = new ContextMenuStrip
            {
                Renderer = new ModernMenuRenderer(),
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Font = Theme.Base,
                ShowImageMargin = false,
                Padding = new Padding(4)
            };

            var grab = new ToolStripMenuItem("Grab text (OCR)", null, GrabText) { Padding = new Padding(4, 3, 4, 3) };
            var pick = new ToolStripMenuItem("Pick Text", null, PickText) { Padding = new Padding(4, 3, 4, 3) };
            var capture = new ToolStripMenuItem("Screen Capture", null, ScreenCapture) { Padding = new Padding(4, 3, 4, 3) };
            var gif = new ToolStripMenuItem("Record GIF", null, RecordGif) { Padding = new Padding(4, 3, 4, 3) };
            var video = new ToolStripMenuItem("Record Video", null, RecordVideo) { Padding = new Padding(4, 3, 4, 3) };
            var note = new ToolStripMenuItem("New Note", null, NewNote) { Padding = new Padding(4, 3, 4, 3) };

            // each feature shows its current hot key, so the menu doubles as a cheat sheet
            grab.ShortcutKeyDisplayString = HotKeyDisplay(Properties.Settings.Default.OcrHotKey, Properties.Settings.Default.OcrHotKeyModifier);
            pick.ShortcutKeyDisplayString = HotKeyDisplay(Properties.Settings.Default.TextPickHotKey, Properties.Settings.Default.TextPickHotKeyModifier);
            capture.ShortcutKeyDisplayString = HotKeyDisplay(Properties.Settings.Default.CaptureHotKey, Properties.Settings.Default.CaptureHotKeyModifier);
            gif.ShortcutKeyDisplayString = HotKeyDisplay(Properties.Settings.Default.GifHotKey, Properties.Settings.Default.GifHotKeyModifier);
            video.ShortcutKeyDisplayString = HotKeyDisplay(Properties.Settings.Default.VideoHotKey, Properties.Settings.Default.VideoHotKeyModifier);
            note.ShortcutKeyDisplayString = HotKeyDisplay(Properties.Settings.Default.NoteHotKey, Properties.Settings.Default.NoteHotKeyModifier);
            var keySettings = new ToolStripMenuItem("Key Setting", null, Settings) { Padding = new Padding(4, 3, 4, 3) };
            var ocrSettings = new ToolStripMenuItem("OCR Setting", null, OcrSettings) { Padding = new Padding(4, 3, 4, 3) };
            var captureSettings = new ToolStripMenuItem("Capture Setting", null, CaptureSettings) { Padding = new Padding(4, 3, 4, 3) };
            var gifSettings = new ToolStripMenuItem("GIF Setting", null, GifSettings) { Padding = new Padding(4, 3, 4, 3) };
            var videoSettings = new ToolStripMenuItem("Video Setting", null, VideoSettings) { Padding = new Padding(4, 3, 4, 3) };
            var noteSettings = new ToolStripMenuItem("Note Setting", null, NoteSettings) { Padding = new Padding(4, 3, 4, 3) };
            var about = new ToolStripMenuItem("About", null, About) { Padding = new Padding(4, 3, 4, 3) };
            var exit = new ToolStripMenuItem("Exit", null, Exit) { Padding = new Padding(4, 3, 4, 3) };
            menu.Items.Add(grab);
            menu.Items.Add(pick);
            menu.Items.Add(capture);
            menu.Items.Add(gif);
            menu.Items.Add(video);
            menu.Items.Add(note);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(keySettings);
            menu.Items.Add(ocrSettings);
            menu.Items.Add(captureSettings);
            menu.Items.Add(gifSettings);
            menu.Items.Add(videoSettings);
            menu.Items.Add(noteSettings);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(about);
            menu.Items.Add(exit);
            return menu;
        }
        private void HotKeyManager_HotKeyPressed(object sender, HotKeyEventArgs e)
        {
            // both hot keys raise this event: only react to the typing one
            if (!Matches(e, Properties.Settings.Default.HotKey, Properties.Settings.Default.HotKeyModifier)) return;

            StopHotKey();
            // act on the key DOWN: the crosshair appears while the hot key is still held;
            // StartTyping waits out the modifiers itself before any keystroke is injected
            switch((HotKeyMode) Properties.Settings.Default.HotKeyMode)
            {
                case HotKeyMode.Target:
                    StartTrack();
                    break;
                case HotKeyMode.JustGo:
                    StartTyping();
                    break;
            }
        }
        private void HotKeyManager_EscapePressed(object sender, HotKeyEventArgs e)
        {
            if (e.Key != Keys.Escape) return;
            StopHotKey();
            lock (this)
            {
                _stop.Cancel();// stop running task
            }
            lock(this)
            {
                _stop = new CancellationTokenSource(); // new source for next time
            }
        }
        void StartTrack()
        {
            if (_hook == null)
            {
                uint[] Cursors = { Native.NORMAL, Native.IBEAM, Native.HAND };

                for (int i = 0; i < Cursors.Length; i++)
                    Native.SetSystemCursor(Native.CopyIcon(Native.LoadCursor(IntPtr.Zero, (int)Native.CROSS)), Cursors[i]);
                _hook = Hook.GlobalEvents();
                _hook.MouseUp += _hook_MouseUp;
                _hook.KeyDown += _hook_KeyDown;
            }
        }


        private void _hook_KeyDown(object sender, KeyEventArgs e)
        {
            if(e.KeyCode == Keys.Escape)
            {
                EndTrack();
                StartHotKey();
            }
        }

        void EndTrack()
        {
            if (_hook != null)
            {
                _hook.MouseUp -= _hook_MouseUp;
                _hook.KeyDown -= _hook_KeyDown;
                _hook.Dispose();
                _hook = null;
                Native.SystemParametersInfo(0x0057, 0, null, 0);
            }
        }

        private void _notify_MouseDown(object sender, MouseEventArgs e)
        {
            switch(e.Button)

            {
                //case MouseButtons.Middle:
                case MouseButtons.Left: // this is a lie, we only get left after mouse released

                    if (!_settingsOpen)
                    {
                        StartTrack();
                    }                    
                    break;
            }
        }
        private void _hook_MouseUp(object sender, MouseEventArgs e)
        {
            switch (e.Button)
            {
                case MouseButtons.Left:
                //case MouseButtons.Middle:
                    EndTrack();
                    StartTyping();
                    break;
            }
        }
        void StartTyping()
        {
            // never type while the hot key's modifiers are still down - held Ctrl/Alt/Shift
            // would combine with the injected keystrokes and corrupt them
            while (Native.IsModifierKeyPressed())
            {
                Thread.Sleep(50);
            }
            string clip;
            try
            {
                clip = Clipboard.GetText();
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                SystemSounds.Beep.Play();
                StartHotKey();
                return;
            }

            // whatever window is under focus right now should be the target

            if (Properties.Settings.Default.Confirm && clip != null && clip.Length > Properties.Settings.Default.ConfirmOver)
            {
                SystemSounds.Beep.Play();
                var w = Native.GetForegroundWindow();
                if (!ModernDialog.Confirm(
                        "Type this into the target?",
                        $"{clip.Length:N0} characters will be typed into\r\n\"{Native.GetText(w).First(50)}\".\r\n\r\nPress Esc at any time to stop.",
                        "Type it", "Cancel"))
                {
                    StartHotKey();// resume normal hotkey listening
                    Native.SetForegroundWindow(w);
                    return;
                }
                Native.SetForegroundWindow(w);
            }
            TypeText(clip);
        }

        /// <summary>
        /// Types a string into whatever window has focus, using the configured method.
        /// Shared by clipboard pasting and by the OCR "type it out" output.
        /// </summary>
        void TypeText(string clip)
        {
            // capture cancel token and vars before running task in case they change
            // this doesn't matter for _notify because we don't change it, but it's good practice
            CancellationToken cancel;
            NotifyIcon notify;
            lock(this)
            {
                cancel = _stop.Token;
                notify = _notify;
            }
            Task.Run(() =>
            {
                // check if it's my window
                //IntPtr hwnd = Native.WindowFromPoint(e.X, e.Y);
                // ... we don't have a window yet
                if (string.IsNullOrEmpty(clip))
                {
                    // nothing to paste
                    SystemSounds.Beep.Play();
                }
                else
                {
                    var icon = notify.Icon;
                    try
                    {
                        var traySize = SystemInformation.SmallIconSize;
                        notify.Icon = new System.Drawing.Icon(Properties.Resources.Typing, traySize.Width, traySize.Height);
                        int startDelayMS = Properties.Settings.Default.StartDelayMS;
                        Thread.Sleep(100 + startDelayMS);
                        StartHotKeyEscape();
                        int keyDelayMS = Properties.Settings.Default.KeyDelayMS;
                        var method = (TypeMethod)Properties.Settings.Default.TypeMethod;
                        IList<string> list = PrepareKeystrokes(clip, method);

                        if (TypeMethod.AutoIt_Send == method)
                        {
                            AutoIt.AutoItX.AutoItSetOption("SendKeyDelay", 0);
                        }
                        foreach (var s in list)
                        {
                            switch (method)
                            {

                                case TypeMethod.AutoIt_Send:
                                    AutoIt.AutoItX.Send(s, 1);
                                    break;
                                case TypeMethod.Forms_SendKeys:
                                    SendKeys.SendWait(s);
                                    break;
                                case TypeMethod.SendInput_ScanCode:
                                    // Send via scan codes with ALT code fallback - works with VM consoles
                                    foreach (char c in s)
                                    {
                                        Native.SendCharViaScanCode(c);
                                    }
                                    break;
                            }
                            Thread.Sleep(keyDelayMS);
                            if (cancel.IsCancellationRequested)
                            {
                                break;//stop typing early
                            }
                        }
                    }
                    catch(Exception)
                    {
                        SystemSounds.Beep.Play();
                    }
                    finally
                    {
                        notify.Icon = icon;
                    }
                }
                StartHotKey();// resume normal hotkey listening
            });

        }
        IList<string> PrepareKeystrokes(string raw, TypeMethod method)
        {
            var list = new List<string>();
            var specials = @"{}[]+^%~()";
            raw = raw.Replace("\r\n", "\r");// both typing methods treat each of these as "hit enter" in notepads, and Word treats \n as "next page" somehow.
            foreach(char c in raw)
            {
                if(method == TypeMethod.Forms_SendKeys && (-1 != specials.IndexOf(c)))
                {
                    list.Add("{" + c.ToString() + "}");
                }
                else
                {
                    list.Add(c.ToString());
                }
            }
            return list;
        }
        
        /// <summary>"Ctrl+Alt+G" for the tray menu, or null when the hot key is unset or invalid.</summary>
        static string HotKeyDisplay(string letter, int modifiers)
        {
            if (string.IsNullOrEmpty(letter)) return null;
            Keys key;
            if (!Enum.TryParse(letter, out key)) return null;
            return DescribeHotKey(key, (KeyModifiers)(modifiers & 0xF)).Replace(" + ", "+");
        }

        /// <summary>Hot keys may have changed: rebuild the menu so the labels stay honest.</summary>
        void RefreshTrayMenu()
        {
            var old = _notify.ContextMenuStrip;
            _notify.ContextMenuStrip = BuildTrayMenu();
            if (old != null) old.Dispose();
        }

        /// <summary>"Ctrl + Alt + V", for the dialogs below.</summary>
        static string DescribeHotKey(Keys key, KeyModifiers modifiers)
        {
            var parts = new List<string>();
            if ((modifiers & KeyModifiers.Control) != 0) parts.Add("Ctrl");
            if ((modifiers & KeyModifiers.Alt) != 0) parts.Add("Alt");
            if ((modifiers & KeyModifiers.Shift) != 0) parts.Add("Shift");
            if ((modifiers & KeyModifiers.Windows) != 0) parts.Add("Win");
            parts.Add(key.ToString());
            return string.Join(" + ", parts);
        }

        /// <summary>
        /// Registers a hot key, and when another application already owns the combination, asks whether
        /// MicroApp should take it over. The answer is remembered per combination, so the question is
        /// asked once and not on every re-registration. Returns null when nothing was registered.
        /// </summary>
        /// <param name="what">Feature name for the dialog heading, e.g. "Typing".</param>
        /// <param name="takeOverSetting">Name of the setting holding the combination the user agreed to take over.</param>
        static int? RegisterOrTakeOver(string what, Keys key, KeyModifiers modifiers, string takeOverSetting)
        {
            int id, error;
            if (HotKeyManager.TryRegisterHotKey(key, modifiers, out id, out error)) return id;

            string combo = DescribeHotKey(key, modifiers);
            if (error != HotKeyManager.ERROR_HOTKEY_ALREADY_REGISTERED)
            {
                ModernDialog.Info(what + " hot key unavailable", combo + " could not be registered.\r\n\r\nWindows error " + error + ".");
                return null;
            }

            bool alreadyAgreed = string.Equals((string)Properties.Settings.Default[takeOverSetting], combo, StringComparison.Ordinal);
            if (!alreadyAgreed)
            {
                bool takeIt = ModernDialog.Confirm(
                    what + " hot key is taken",
                    combo + " is already registered by another application.\r\n\r\nUse it for MicroApp instead? MicroApp will see the key first, and the other app will stop receiving it.",
                    "Yes, use it here",
                    "Leave it");
                if (!takeIt) return null;
                Properties.Settings.Default[takeOverSetting] = combo;
                Properties.Settings.Default.Save();
            }

            try
            {
                return HotKeyManager.RegisterHotKeyOverride(key, modifiers);
            }
            catch (Exception e)
            {
                ModernDialog.Info(what + " hot key unavailable", combo + " could not be taken over.\r\n\r\n" + e.Message);
                return null;
            }
        }

        void StartHotKey()
        {
            StopHotKey();
            var hotkeyLetter = Properties.Settings.Default.HotKey;
            if (!string.IsNullOrEmpty(hotkeyLetter))
            {
                try
                {
                    Keys HotKey = (Keys)Enum.Parse(typeof(Keys), hotkeyLetter);
                    _usingHotKey = RegisterOrTakeOver("Typing", HotKey, (KeyModifiers)Properties.Settings.Default.HotKeyModifier, "HotKeyTakeOver");
                    if (_usingHotKey.HasValue)
                    {
                        _currentHotKeyHandler = new EventHandler<HotKeyEventArgs>(HotKeyManager_HotKeyPressed);
                        HotKeyManager.HotKeyPressed += _currentHotKeyHandler;
                    }
                }
                catch(Exception e)
                {
                    ModernDialog.Info("Hot key unavailable", "Another app is probably using it.\r\n\r\n" + e.Message);
                }
            }
        }
        void StartHotKeyEscape()
        {
            StopHotKey();
            try
            {
                _usingHotKey = HotKeyManager.RegisterHotKey(Keys.Escape, KeyModifiers.None);
                _currentHotKeyHandler = new EventHandler<HotKeyEventArgs>(HotKeyManager_EscapePressed);
                HotKeyManager.HotKeyPressed += _currentHotKeyHandler;
            }
            catch (Exception e)
            {
                ModernDialog.Info("Hot key unavailable", "Another app is probably using it.\r\n\r\n" + e.Message);
            }
        }
        /// <summary>True when this hot key event matches the given configured hot key.</summary>
        static bool Matches(HotKeyEventArgs e, string letter, int modifiers)
        {
            if (string.IsNullOrEmpty(letter)) return false;
            Keys key;
            if (!Enum.TryParse(letter, out key)) return false;
            return e.Key == key && ((int)e.Modifiers & 0xF) == (modifiers & 0xF);
        }

        void StartOcrHotKey()
        {
            StopOcrHotKey();
            var letter = Properties.Settings.Default.OcrHotKey;
            if (string.IsNullOrEmpty(letter)) return;
            try
            {
                Keys key = (Keys)Enum.Parse(typeof(Keys), letter);
                _ocrHotKey = RegisterOrTakeOver("OCR", key, (KeyModifiers)Properties.Settings.Default.OcrHotKeyModifier, "OcrHotKeyTakeOver");
                if (!_ocrHotKey.HasValue) return;
                _ocrHotKeyHandler = new EventHandler<HotKeyEventArgs>(HotKeyManager_OcrHotKeyPressed);
                HotKeyManager.HotKeyPressed += _ocrHotKeyHandler;
            }
            catch (Exception e)
            {
                ModernDialog.Info("OCR hot key unavailable", "Another app is probably using it.\r\n\r\n" + e.Message);
            }
        }

        void StopOcrHotKey()
        {
            if (_ocrHotKey.HasValue)
            {
                HotKeyManager.HotKeyPressed -= _ocrHotKeyHandler;
                HotKeyManager.UnregisterHotKey(_ocrHotKey.Value);
            }
            _ocrHotKey = null;
            _ocrHotKeyHandler = null;
        }

        private void HotKeyManager_OcrHotKeyPressed(object sender, HotKeyEventArgs e)
        {
            if (!Matches(e, Properties.Settings.Default.OcrHotKey, Properties.Settings.Default.OcrHotKeyModifier)) return;

            BeginOcrCapture();   // on the key down - the overlay opens while the keys are held
        }

        void GrabText(object sender, EventArgs e)
        {
            BeginOcrCapture();
        }

        /// <summary>Runs the capture on the tray thread, wherever it was triggered from.</summary>
        void BeginOcrCapture()
        {
            if (_ocrBusy) return;
            if (_sync.InvokeRequired)
            {
                _sync.BeginInvoke(new Action(RunOcrCapture));
            }
            else
            {
                RunOcrCapture();
            }
        }

        /// <summary>
        /// Crosshair -> drag a region -> read it -> hand the text off per the OCR settings.
        /// </summary>
        void RunOcrCapture()
        {
            if (_ocrBusy) return;
            _ocrBusy = true;

            // remember where the user was, so "type it out" lands in the right window
            IntPtr target = Native.GetForegroundWindow();
            try
            {
                Bitmap region = RegionCaptureOverlay.SelectRegion();
                if (region == null) return;   // cancelled

                string text;
                var previous = _notify.Icon;
                using (region)
                {
                    try
                    {
                        var traySize = SystemInformation.SmallIconSize;
                        _notify.Icon = new System.Drawing.Icon(Properties.Resources.Typing, traySize.Width, traySize.Height);
                        text = OcrService.Recognize(region,
                                                    Properties.Settings.Default.OcrLanguage,
                                                    Properties.Settings.Default.OcrKeepLines);
                    }
                    catch (Exception ex)
                    {
                        SystemSounds.Beep.Play();
                        ModernDialog.Info("Could not read that", ex.Message);
                        return;
                    }
                    finally
                    {
                        _notify.Icon = previous;
                    }
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    SystemSounds.Beep.Play();
                    Toast.Show("No text found in that selection.");
                    return;
                }

                DeliverOcrText(text, target);
            }
            finally
            {
                _ocrBusy = false;
            }
        }

        void DeliverOcrText(string text, IntPtr target)
        {
            switch ((OcrOutput)Properties.Settings.Default.OcrOutput)
            {
                case OcrOutput.Preview:
                    using (var preview = new OcrResultForm(text))
                    {
                        preview.ShowDialog();
                        if (preview.TypeRequested)
                        {
                            Native.SetForegroundWindow(target);
                            Thread.Sleep(120);
                            TypeText(preview.CapturedText);
                        }
                    }
                    break;

                case OcrOutput.Type:
                    Native.SetForegroundWindow(target);
                    Thread.Sleep(120);
                    TypeText(text);
                    break;

                default:
                    if (SetClipboard(text))
                    {
                        Toast.Show($"Copied {text.Length:N0} characters to the clipboard.");
                    }
                    else
                    {
                        SystemSounds.Beep.Play();
                        ModernDialog.Info("Clipboard is busy", "Another app is holding the clipboard. Try again.");
                    }
                    break;
            }
        }

        /// <summary>Clipboard writes fail while another app holds it; retry briefly.</summary>
        static bool SetClipboard(string text)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Clipboard.SetText(text);
                    return true;
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    Thread.Sleep(80);
                }
            }
            return false;
        }

        void StartCaptureHotKey()
        {
            StopCaptureHotKey();
            var letter = Properties.Settings.Default.CaptureHotKey;
            if (string.IsNullOrEmpty(letter)) return;
            try
            {
                Keys key = (Keys)Enum.Parse(typeof(Keys), letter);
                _captureHotKey = RegisterOrTakeOver("Screen capture", key, (KeyModifiers)Properties.Settings.Default.CaptureHotKeyModifier, "CaptureHotKeyTakeOver");
                if (!_captureHotKey.HasValue) return;
                _captureHotKeyHandler = new EventHandler<HotKeyEventArgs>(HotKeyManager_CaptureHotKeyPressed);
                HotKeyManager.HotKeyPressed += _captureHotKeyHandler;
            }
            catch (Exception e)
            {
                ModernDialog.Info("Capture hot key unavailable", "Another app is probably using it.\r\n\r\n" + e.Message);
            }
        }

        void StopCaptureHotKey()
        {
            if (_captureHotKey.HasValue)
            {
                HotKeyManager.HotKeyPressed -= _captureHotKeyHandler;
                HotKeyManager.UnregisterHotKey(_captureHotKey.Value);
            }
            _captureHotKey = null;
            _captureHotKeyHandler = null;
        }

        private void HotKeyManager_CaptureHotKeyPressed(object sender, HotKeyEventArgs e)
        {
            if (!Matches(e, Properties.Settings.Default.CaptureHotKey, Properties.Settings.Default.CaptureHotKeyModifier)) return;

            BeginScreenCapture();   // on the key down
        }

        void ScreenCapture(object sender, EventArgs e)
        {
            BeginScreenCapture();
        }

        void BeginScreenCapture()
        {
            if (_ocrBusy) return;
            if (_sync.InvokeRequired)
            {
                _sync.BeginInvoke(new Action(RunScreenCapture));
            }
            else
            {
                RunScreenCapture();
            }
        }

        /// <summary>
        /// Crosshair -> drag (or click, with a pixel lock) -> the picture goes to the
        /// clipboard, to a PNG, or both.
        /// </summary>
        void RunScreenCapture()
        {
            if (_ocrBusy) return;
            _ocrBusy = true;
            try
            {
                var constraint = CaptureConstraint.FromSettings();
                string hint = constraint.LockPixel
                    ? $"Click to grab a {constraint.PixelSize.Width} x {constraint.PixelSize.Height} shot.   Esc cancels."
                    : constraint.LockRatio
                        ? $"Drag a {constraint.RatioName} box.   Esc cancels."
                        : "Drag the area you want to capture.   Esc cancels.";

                using (Bitmap shot = RegionCaptureOverlay.SelectRegion(constraint, hint))
                {
                    if (shot == null) return;   // cancelled
                    DeliverCapture(shot);
                }
            }
            finally
            {
                _ocrBusy = false;
            }
        }

        void DeliverCapture(Bitmap shot)
        {
            var output = (CaptureOutput)Properties.Settings.Default.CaptureOutput;
            bool copied = false;
            string savedPath = null;

            if (output == CaptureOutput.Clipboard || output == CaptureOutput.Both)
            {
                copied = SetClipboardImage(shot);
            }

            if (output == CaptureOutput.File || output == CaptureOutput.Both)
            {
                try
                {
                    string folder = CaptureSettingsForm.DefaultFolder();
                    System.IO.Directory.CreateDirectory(folder);
                    savedPath = System.IO.Path.Combine(
                        folder, "MicroApp-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
                    shot.Save(savedPath, System.Drawing.Imaging.ImageFormat.Png);
                }
                catch (Exception ex)
                {
                    SystemSounds.Beep.Play();
                    ModernDialog.Info("Could not save the capture", ex.Message);
                    savedPath = null;
                }
            }

            string what = $"{shot.Width} x {shot.Height}";
            if (savedPath != null && copied)
            {
                Toast.Show($"{what} copied and saved to\r\n{savedPath}");
            }
            else if (savedPath != null)
            {
                Toast.Show($"{what} saved to\r\n{savedPath}");
            }
            else if (copied)
            {
                Toast.Show($"{what} image copied to the clipboard.");
            }
            else
            {
                SystemSounds.Beep.Play();
                ModernDialog.Info("Clipboard is busy", "Another app is holding the clipboard. Try again.");
            }
        }

        static bool SetClipboardImage(Bitmap image)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Clipboard.SetImage(image);
                    return true;
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    Thread.Sleep(80);
                }
            }
            return false;
        }

        void StartGifHotKey()
        {
            StopGifHotKey();
            var letter = Properties.Settings.Default.GifHotKey;
            if (string.IsNullOrEmpty(letter)) return;
            try
            {
                Keys key = (Keys)Enum.Parse(typeof(Keys), letter);
                _gifHotKey = RegisterOrTakeOver("GIF", key, (KeyModifiers)Properties.Settings.Default.GifHotKeyModifier, "GifHotKeyTakeOver");
                if (!_gifHotKey.HasValue) return;
                _gifHotKeyHandler = new EventHandler<HotKeyEventArgs>(HotKeyManager_GifHotKeyPressed);
                HotKeyManager.HotKeyPressed += _gifHotKeyHandler;
            }
            catch (Exception e)
            {
                ModernDialog.Info("GIF hot key unavailable", "Another app is probably using it.\r\n\r\n" + e.Message);
            }
        }

        void StopGifHotKey()
        {
            if (_gifHotKey.HasValue)
            {
                HotKeyManager.HotKeyPressed -= _gifHotKeyHandler;
                HotKeyManager.UnregisterHotKey(_gifHotKey.Value);
            }
            _gifHotKey = null;
            _gifHotKeyHandler = null;
        }

        private void HotKeyManager_GifHotKeyPressed(object sender, HotKeyEventArgs e)
        {
            if (!Matches(e, Properties.Settings.Default.GifHotKey, Properties.Settings.Default.GifHotKeyModifier)) return;

            // pressing the hot key again while recording is the natural way to stop
            if (_recorder != null)
            {
                BeginStopRecording();
                return;
            }
            BeginGifRecording();   // on the key down
        }

        void RecordGif(object sender, EventArgs e)
        {
            if (_recorder != null) { BeginStopRecording(); return; }
            BeginGifRecording();
        }

        void BeginGifRecording()
        {
            if (_ocrBusy || _recorder != null || _videoRecorder != null) return;
            if (_sync.InvokeRequired) _sync.BeginInvoke(new Action(StartGifRecording));
            else StartGifRecording();
        }

        void BeginStopRecording()
        {
            if (_sync.InvokeRequired) _sync.BeginInvoke(new Action(FinishGifRecording));
            else FinishGifRecording();
        }

        /// <summary>Pick a region, then roll: frames stream to disk until Esc or the time limit.</summary>
        void StartGifRecording()
        {
            if (_ocrBusy || _recorder != null || _videoRecorder != null) return;
            _ocrBusy = true;

            Rectangle region;
            try
            {
                var constraint = CaptureConstraint.FromGifSettings();
                string hint = constraint.LockPixel
                    ? $"Click to record a {constraint.PixelSize.Width} x {constraint.PixelSize.Height} GIF.   Esc cancels."
                    : constraint.LockRatio
                        ? $"Drag a {constraint.RatioName} area to record.   Esc cancels."
                        : "Drag the area to record as a GIF.   Esc cancels.";

                using (Bitmap shot = RegionCaptureOverlay.SelectRegion(constraint, hint))
                {
                    if (shot == null) return;                       // cancelled
                    region = RegionCaptureOverlay.LastRegion;
                }
            }
            finally
            {
                _ocrBusy = false;
            }
            if (region.Width < 8 || region.Height < 8) return;

            int fps = Properties.Settings.Default.GifFps;
            int seconds = Properties.Settings.Default.GifSeconds;
            string folder = GifSettingsForm.DefaultFolder();
            string path;
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                path = System.IO.Path.Combine(folder, "MicroApp-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".gif");
            }
            catch (Exception ex)
            {
                ModernDialog.Info("Could not start recording", ex.Message);
                return;
            }

            _recorder = new GifRecorder(region, path, fps, seconds);
            _indicator = new RecordingIndicator(region, seconds);
            _indicator.StopRequested += (s, e) => BeginStopRecording();
            _indicator.Show();

            // Esc anywhere stops the recording
            _recordHook = Hook.GlobalEvents();
            _recordHook.KeyDown += _recordHook_KeyDown;

            var traySize = SystemInformation.SmallIconSize;
            _notify.Icon = new System.Drawing.Icon(Properties.Resources.Typing, traySize.Width, traySize.Height);

            _recorder.Start();
            Toast.Show($"Recording {region.Width} x {region.Height} at {fps} fps. Esc to stop.");

            // stop by itself once the time limit is up
            var limit = new System.Windows.Forms.Timer { Interval = Math.Max(1000, seconds * 1000 + 400) };
            limit.Tick += (s, e) => { limit.Stop(); limit.Dispose(); FinishGifRecording(); };
            limit.Start();
        }

        private void _recordHook_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) BeginStopRecording();
        }

        void FinishGifRecording()
        {
            var recorder = _recorder;
            if (recorder == null) return;
            _recorder = null;

            if (_recordHook != null)
            {
                _recordHook.KeyDown -= _recordHook_KeyDown;
                _recordHook.Dispose();
                _recordHook = null;
            }
            if (_indicator != null)
            {
                _indicator.Close();
                _indicator.Dispose();
                _indicator = null;
            }

            recorder.Stop();
            int frames = recorder.FrameCount;
            string path = recorder.Path;
            recorder.Dispose();

            var traySize = SystemInformation.SmallIconSize;
            bool darkTray = ThemeHelper.IsSystemDark;
            _notify.Icon = new System.Drawing.Icon(
                darkTray ? Properties.Resources.Target : Properties.Resources.TargetDark,
                traySize.Width, traySize.Height);

            if (frames == 0)
            {
                SystemSounds.Beep.Play();
                Toast.Show("Nothing was recorded.");
                return;
            }

            long size = 0;
            try { size = new System.IO.FileInfo(path).Length; } catch (Exception) { }
            string note = $"GIF saved: {frames} frames, {size / 1024:N0} KB\r\n{System.IO.Path.GetFileName(path)}";

            switch ((GifOutput)Properties.Settings.Default.GifOutput)
            {
                case GifOutput.SaveAndCopyPath:
                    if (SetClipboard(path)) note += "\r\nPath copied.";
                    break;
                case GifOutput.SaveAndOpen:
                    try { System.Diagnostics.Process.Start(path); }
                    catch (Exception ex) { note += "\r\nCould not open it: " + ex.Message; }
                    break;
            }
            Toast.Show(note);
        }

        void StartVideoHotKey()
        {
            StopVideoHotKey();
            var letter = Properties.Settings.Default.VideoHotKey;
            if (string.IsNullOrEmpty(letter)) return;
            try
            {
                Keys key = (Keys)Enum.Parse(typeof(Keys), letter);
                _videoHotKey = RegisterOrTakeOver("Video", key, (KeyModifiers)Properties.Settings.Default.VideoHotKeyModifier, "VideoHotKeyTakeOver");
                if (!_videoHotKey.HasValue) return;
                _videoHotKeyHandler = new EventHandler<HotKeyEventArgs>(HotKeyManager_VideoHotKeyPressed);
                HotKeyManager.HotKeyPressed += _videoHotKeyHandler;
            }
            catch (Exception e)
            {
                ModernDialog.Info("Video hot key unavailable", "Another app is probably using it.\r\n\r\n" + e.Message);
            }
        }

        void StopVideoHotKey()
        {
            if (_videoHotKey.HasValue)
            {
                HotKeyManager.HotKeyPressed -= _videoHotKeyHandler;
                HotKeyManager.UnregisterHotKey(_videoHotKey.Value);
            }
            _videoHotKey = null;
            _videoHotKeyHandler = null;
        }

        private void HotKeyManager_VideoHotKeyPressed(object sender, HotKeyEventArgs e)
        {
            if (!Matches(e, Properties.Settings.Default.VideoHotKey, Properties.Settings.Default.VideoHotKeyModifier)) return;

            // pressing the hot key again while recording is the natural way to stop
            if (_videoRecorder != null)
            {
                BeginStopVideoRecording();
                return;
            }
            BeginVideoRecording();   // on the key down
        }

        void RecordVideo(object sender, EventArgs e)
        {
            if (_videoRecorder != null) { BeginStopVideoRecording(); return; }
            BeginVideoRecording();
        }

        void BeginVideoRecording()
        {
            if (_ocrBusy || _videoRecorder != null || _recorder != null) return;
            if (_sync.InvokeRequired) _sync.BeginInvoke(new Action(StartVideoRecording));
            else StartVideoRecording();
        }

        void BeginStopVideoRecording()
        {
            if (_sync.InvokeRequired) _sync.BeginInvoke(new Action(FinishVideoRecording));
            else FinishVideoRecording();
        }

        /// <summary>Pick a region, then roll: an MP4 with sound streams to disk until Esc or the time limit.</summary>
        void StartVideoRecording()
        {
            if (_ocrBusy || _videoRecorder != null || _recorder != null) return;
            _ocrBusy = true;

            Rectangle region;
            try
            {
                var constraint = CaptureConstraint.FromVideoSettings();
                string hint = constraint.LockPixel
                    ? $"Click to record a {constraint.PixelSize.Width} x {constraint.PixelSize.Height} video.   Esc cancels."
                    : constraint.LockRatio
                        ? $"Drag a {constraint.RatioName} area to record.   Esc cancels."
                        : "Drag the area to record as a video.   Esc cancels.";

                using (Bitmap shot = RegionCaptureOverlay.SelectRegion(constraint, hint))
                {
                    if (shot == null) return;                       // cancelled
                    region = RegionCaptureOverlay.LastRegion;
                }
            }
            finally
            {
                _ocrBusy = false;
            }
            if (region.Width < 8 || region.Height < 8) return;

            int fps = Properties.Settings.Default.VideoFps;
            string folder = VideoSettingsForm.DefaultFolder();
            string path;
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                path = System.IO.Path.Combine(folder, "MicroApp-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".mp4");
            }
            catch (Exception ex)
            {
                ModernDialog.Info("Could not start recording", ex.Message);
                return;
            }

            try
            {
                // no time limit: the recording runs until it is saved or Esc is pressed
                _videoRecorder = new VideoRecorder(region, path, fps, 0,
                    (VideoQuality)Properties.Settings.Default.VideoQuality,
                    (VideoAudioSource)Properties.Settings.Default.VideoAudioSource);
            }
            catch (Exception ex)
            {
                ModernDialog.Info("Could not start recording",
                    "The Windows video encoder is unavailable.\r\n(On Windows N, install the Media Feature Pack.)\r\n\r\n" + ex.Message);
                return;
            }

            _videoIndicator = new VideoRecordingIndicator(region);
            _videoIndicator.PauseToggled += (s, paused) =>
            {
                var recorder = _videoRecorder;
                if (recorder == null) return;
                if (paused) recorder.Pause(); else recorder.Resume();
                if (_videoFrame != null) _videoFrame.SetPaused(paused);
            };
            _videoIndicator.SaveRequested += (s, e) => BeginStopVideoRecording();
            _videoIndicator.Show();

            // the region stays marked until the video is saved
            _videoFrame = new RecordingRegionFrame(region);
            _videoFrame.Show();

            // Esc anywhere stops the recording
            _videoHook = Hook.GlobalEvents();
            _videoHook.KeyDown += _videoHook_KeyDown;

            var traySize = SystemInformation.SmallIconSize;
            _notify.Icon = new System.Drawing.Icon(Properties.Resources.Typing, traySize.Width, traySize.Height);

            _videoRecorder.Start();
            string note = $"Recording {region.Width & ~1} x {region.Height & ~1} at {fps} fps. Save on the badge or Esc to stop.";
            if (_videoRecorder.AudioMissing) note += "\r\nNo audio device found: recording without sound.";
            Toast.Show(note);
        }

        private void _videoHook_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) BeginStopVideoRecording();
        }

        void FinishVideoRecording()
        {
            var recorder = _videoRecorder;
            if (recorder == null) return;
            _videoRecorder = null;

            if (_videoHook != null)
            {
                _videoHook.KeyDown -= _videoHook_KeyDown;
                _videoHook.Dispose();
                _videoHook = null;
            }
            if (_videoIndicator != null)
            {
                _videoIndicator.Close();
                _videoIndicator.Dispose();
                _videoIndicator = null;
            }
            if (_videoFrame != null)
            {
                _videoFrame.Close();
                _videoFrame.Dispose();
                _videoFrame = null;
            }

            recorder.Stop();
            int frames = recorder.FrameCount;
            string path = recorder.Path;
            recorder.Dispose();   // finalises the MP4 index; skipping it leaves the file unplayable
            string error = recorder.Error;

            var traySize = SystemInformation.SmallIconSize;
            bool darkTray = ThemeHelper.IsSystemDark;
            _notify.Icon = new System.Drawing.Icon(
                darkTray ? Properties.Resources.Target : Properties.Resources.TargetDark,
                traySize.Width, traySize.Height);

            // report the encoder error even when no frame made it: "Nothing was
            // recorded" alone would hide the real reason
            if (error != null)
            {
                SystemSounds.Beep.Play();
                ModernDialog.Info("Recording problem",
                    frames == 0
                        ? "The video encoder failed before anything could be recorded.\r\n\r\n" + error
                        : "The video encoder reported an error; the file may be incomplete.\r\n\r\n" + error);
            }

            if (frames == 0)
            {
                if (error == null)
                {
                    SystemSounds.Beep.Play();
                    Toast.Show("Nothing was recorded.");
                }
                try { System.IO.File.Delete(path); } catch (Exception) { }
                return;
            }

            long size = 0;
            try { size = new System.IO.FileInfo(path).Length; } catch (Exception) { }
            string note = $"Video saved: {frames} frames, {size / 1024:N0} KB\r\n{System.IO.Path.GetFileName(path)}";

            switch ((VideoOutput)Properties.Settings.Default.VideoOutput)
            {
                case VideoOutput.SaveAndCopyPath:
                    if (SetClipboard(path)) note += "\r\nPath copied.";
                    break;
                case VideoOutput.SaveAndOpen:
                    try { System.Diagnostics.Process.Start(path); }
                    catch (Exception ex) { note += "\r\nCould not open it: " + ex.Message; }
                    break;
            }
            Toast.Show(note);
        }

        void StartNoteHotKey()
        {
            StopNoteHotKey();
            var letter = Properties.Settings.Default.NoteHotKey;
            if (string.IsNullOrEmpty(letter)) return;
            try
            {
                Keys key = (Keys)Enum.Parse(typeof(Keys), letter);
                _noteHotKey = RegisterOrTakeOver("Note", key, (KeyModifiers)Properties.Settings.Default.NoteHotKeyModifier, "NoteHotKeyTakeOver");
                if (!_noteHotKey.HasValue) return;
                _noteHotKeyHandler = new EventHandler<HotKeyEventArgs>(HotKeyManager_NoteHotKeyPressed);
                HotKeyManager.HotKeyPressed += _noteHotKeyHandler;
            }
            catch (Exception e)
            {
                ModernDialog.Info("Note hot key unavailable", "Another app is probably using it.\r\n\r\n" + e.Message);
            }
        }

        void StopNoteHotKey()
        {
            if (_noteHotKey.HasValue)
            {
                HotKeyManager.HotKeyPressed -= _noteHotKeyHandler;
                HotKeyManager.UnregisterHotKey(_noteHotKey.Value);
            }
            _noteHotKey = null;
            _noteHotKeyHandler = null;
        }

        private void HotKeyManager_NoteHotKeyPressed(object sender, HotKeyEventArgs e)
        {
            if (!Matches(e, Properties.Settings.Default.NoteHotKey, Properties.Settings.Default.NoteHotKeyModifier)) return;
            BeginNewNote();   // a fresh note on every press
        }

        void NewNote(object sender, EventArgs e)
        {
            BeginNewNote();
        }

        void BeginNewNote()
        {
            if (_settingsOpen) return;
            if (_sync.InvokeRequired) _sync.BeginInvoke(new Action(() => NoteForm.ShowNew()));
            else NoteForm.ShowNew();
        }

        void NoteSettings(object sender, EventArgs e)
        {
            if (_settingsOpen)
            {
                return;
            }
            _settingsOpen = true;
            StopAllHotKeys();
            var settings = new NoteSettingsForm();
            settings.ShowDialog();
            _settingsOpen = false;
            StartAllHotKeys();
            RefreshTrayMenu();
        }

        void StartTextPickHotKey()
        {
            StopTextPickHotKey();
            var letter = Properties.Settings.Default.TextPickHotKey;
            if (string.IsNullOrEmpty(letter)) return;
            try
            {
                Keys key = (Keys)Enum.Parse(typeof(Keys), letter);
                _pickHotKey = RegisterOrTakeOver("Text picker", key, (KeyModifiers)Properties.Settings.Default.TextPickHotKeyModifier, "TextPickHotKeyTakeOver");
                if (!_pickHotKey.HasValue) return;
                _pickHotKeyHandler = new EventHandler<HotKeyEventArgs>(HotKeyManager_TextPickHotKeyPressed);
                HotKeyManager.HotKeyPressed += _pickHotKeyHandler;
            }
            catch (Exception e)
            {
                ModernDialog.Info("Text picker hot key unavailable", "Another app is probably using it.\r\n\r\n" + e.Message);
            }
        }

        void StopTextPickHotKey()
        {
            if (_pickHotKey.HasValue)
            {
                HotKeyManager.HotKeyPressed -= _pickHotKeyHandler;
                HotKeyManager.UnregisterHotKey(_pickHotKey.Value);
            }
            _pickHotKey = null;
            _pickHotKeyHandler = null;
        }

        private void HotKeyManager_TextPickHotKeyPressed(object sender, HotKeyEventArgs e)
        {
            if (!Matches(e, Properties.Settings.Default.TextPickHotKey, Properties.Settings.Default.TextPickHotKeyModifier)) return;

            // pressing the hot key again while picking cancels it
            if (_pickHook != null)
            {
                _sync.BeginInvoke(new Action(CancelTextPick));
                return;
            }
            BeginTextPick();   // on the key down - the "+" cursor appears while the keys are held
        }

        void PickText(object sender, EventArgs e)
        {
            BeginTextPick();
        }

        void BeginTextPick()
        {
            if (_settingsOpen) return;
            if (_sync.InvokeRequired) _sync.BeginInvoke(new Action(StartTextPick));
            else StartTextPick();
        }

        /// <summary>
        /// "+" crosshair everywhere, a live preview of the text under it, and one click to
        /// take that text — read through UI Automation, so it is exact and multi-line, not OCR.
        /// </summary>
        void StartTextPick()
        {
            if (_ocrBusy || _pickHook != null || _recorder != null || _videoRecorder != null) return;

            // "type it out" and the preview's type button should land back here
            _pickTarget = Native.GetForegroundWindow();

            // same "+" cursor treatment the click-to-target picker uses
            uint[] cursors = { Native.NORMAL, Native.IBEAM, Native.HAND };
            for (int i = 0; i < cursors.Length; i++)
                Native.SetSystemCursor(Native.CopyIcon(Native.LoadCursor(IntPtr.Zero, (int)Native.CROSS)), cursors[i]);

            _pickHud = new TextPickerHud();
            _pickOutline = new ElementOutline();
            _pickHud.UpdatePreview(null, Cursor.Position);
            _pickHud.Show();

            _pickHook = Hook.GlobalEvents();
            _pickHook.MouseDownExt += _pickHook_MouseDownExt;
            _pickHook.KeyDown += _pickHook_KeyDown;

            _pickTimer = new System.Windows.Forms.Timer { Interval = 120 };
            _pickTimer.Tick += _pickTimer_Tick;
            _pickTimer.Start();
        }

        void _pickTimer_Tick(object sender, EventArgs e)
        {
            // single-flight: UI Automation lookups can be slow in some apps
            if (_pickPeekBusy || _pickHud == null) return;
            _pickPeekBusy = true;
            Point p = Cursor.Position;
            Task.Run(() =>
            {
                string snippet;
                Rectangle bounds;
                UiTextReader.Peek(p, out snippet, out bounds);
                _sync.BeginInvoke(new Action(() =>
                {
                    _pickPeekBusy = false;
                    if (_pickHud == null) return;   // picking ended meanwhile
                    _pickHud.UpdatePreview(snippet, Cursor.Position);
                    _pickOutline.Outline(bounds);
                }));
            });
        }

        private void _pickHook_MouseDownExt(object sender, MouseEventExtArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                e.Handled = true;   // the click belongs to the picker, not the app underneath
                var p = new Point(e.X, e.Y);
                _sync.BeginInvoke(new Action(() => FinishTextPick(p)));
            }
            else if (e.Button == MouseButtons.Right)
            {
                e.Handled = true;
                _sync.BeginInvoke(new Action(CancelTextPick));
            }
        }

        private void _pickHook_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                _sync.BeginInvoke(new Action(CancelTextPick));
            }
        }

        void CancelTextPick()
        {
            EndTextPick();
        }

        void EndTextPick()
        {
            if (_pickTimer != null)
            {
                _pickTimer.Stop();
                _pickTimer.Dispose();
                _pickTimer = null;
            }
            if (_pickHook != null)
            {
                _pickHook.MouseDownExt -= _pickHook_MouseDownExt;
                _pickHook.KeyDown -= _pickHook_KeyDown;
                _pickHook.Dispose();
                _pickHook = null;
            }
            if (_pickHud != null)
            {
                _pickHud.Close();
                _pickHud.Dispose();
                _pickHud = null;
            }
            if (_pickOutline != null)
            {
                _pickOutline.Close();
                _pickOutline.Dispose();
                _pickOutline = null;
            }
            Native.SystemParametersInfo(0x0057, 0, null, 0);   // restore the real cursors
        }

        void FinishTextPick(Point point)
        {
            if (_pickHook == null) return;   // already cancelled
            EndTextPick();
            IntPtr target = _pickTarget;

            var previous = _notify.Icon;
            var traySize = SystemInformation.SmallIconSize;
            _notify.Icon = new System.Drawing.Icon(Properties.Resources.Typing, traySize.Width, traySize.Height);
            Task.Run(() =>
            {
                string text = null;
                try { text = UiTextReader.ExtractTextAt(point); } catch (Exception) { }
                _sync.BeginInvoke(new Action(() =>
                {
                    _notify.Icon = previous;
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        SystemSounds.Beep.Play();
                        Toast.Show("No text found there.");
                        return;
                    }
                    // hand it off exactly like OCR output: clipboard, preview or type-out
                    DeliverOcrText(text, target);
                }));
            });
        }

        void StopHotKey()
        {
            if(_usingHotKey.HasValue)
            {
                HotKeyManager.HotKeyPressed -= _currentHotKeyHandler;
                HotKeyManager.UnregisterHotKey(_usingHotKey.Value);
            }
            _usingHotKey = null;
            _currentHotKeyHandler = null;
        }

        void Settings(object sender, EventArgs e)
        {
            if (_settingsOpen)
            {
                return;
            }
            _settingsOpen = true;
            StopAllHotKeys();
            var settings = new SettingsForm();
            settings.ShowDialog();
            _settingsOpen = false;
            StartAllHotKeys();
            RefreshTrayMenu();
        }

        void OcrSettings(object sender, EventArgs e)
        {
            if (_settingsOpen)
            {
                return;
            }
            _settingsOpen = true;
            StopAllHotKeys();
            var settings = new OcrSettingsForm();
            settings.ShowDialog();
            _settingsOpen = false;
            StartAllHotKeys();
            RefreshTrayMenu();
        }

        void About(object sender, EventArgs e)
        {
            if (_settingsOpen)
            {
                return;
            }
            _settingsOpen = true;
            StopAllHotKeys();
            using (var about = new AboutForm())
            {
                about.ShowDialog();
            }
            _settingsOpen = false;
            StartAllHotKeys();
            RefreshTrayMenu();
        }

        void GifSettings(object sender, EventArgs e)
        {
            if (_settingsOpen)
            {
                return;
            }
            _settingsOpen = true;
            StopAllHotKeys();
            var settings = new GifSettingsForm();
            settings.ShowDialog();
            _settingsOpen = false;
            StartAllHotKeys();
            RefreshTrayMenu();
        }

        void VideoSettings(object sender, EventArgs e)
        {
            if (_settingsOpen)
            {
                return;
            }
            _settingsOpen = true;
            StopAllHotKeys();
            var settings = new VideoSettingsForm();
            settings.ShowDialog();
            _settingsOpen = false;
            StartAllHotKeys();
            RefreshTrayMenu();
        }

        void CaptureSettings(object sender, EventArgs e)
        {
            if (_settingsOpen)
            {
                return;
            }
            _settingsOpen = true;
            StopAllHotKeys();
            var settings = new CaptureSettingsForm();
            settings.ShowDialog();
            _settingsOpen = false;
            StartAllHotKeys();
            RefreshTrayMenu();
        }

        void StopAllHotKeys()
        {
            StopHotKey();
            StopOcrHotKey();
            StopCaptureHotKey();
            StopGifHotKey();
            StopVideoHotKey();
            StopTextPickHotKey();
            StopNoteHotKey();
        }

        void StartAllHotKeys()
        {
            StartHotKey();
            StartOcrHotKey();
            StartCaptureHotKey();
            StartGifHotKey();
            StartVideoHotKey();
            StartTextPickHotKey();
            StartNoteHotKey();
        }

        void Exit(object sender, EventArgs e)
        {
            if (_recorder != null) FinishGifRecording();
            if (_videoRecorder != null) FinishVideoRecording();
            EndTextPick();
            EndTrack();
            // Hide tray icon, otherwise it will remain shown until user mouses over it
            _notify.Visible = false;
            _notify.Dispose();
            StopAllHotKeys();
            Application.Exit();
        }
    }
}
