using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Threading;

namespace MicroApp
{
    public static class HotKeyManager
    {
        public static event EventHandler<HotKeyEventArgs> HotKeyPressed;

        /// <summary>What RegisterHotKey reports when another application already owns the combination.</summary>
        public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

        public static int RegisterHotKey(Keys key, KeyModifiers modifiers)
        {
            int id, error;
            if (!TryRegisterHotKey(key, modifiers, out id, out error))
            {
                throw new InvalidOperationException($"Failed to register hotkey. The hotkey may already be in use by another application.");
            }
            return id;
        }

        /// <summary>
        /// Registers without throwing. On failure <paramref name="error"/> holds the Win32 error;
        /// ERROR_HOTKEY_ALREADY_REGISTERED means another application owns the combination and
        /// <see cref="RegisterHotKeyOverride"/> is the only way to get it.
        /// </summary>
        public static bool TryRegisterHotKey(Keys key, KeyModifiers modifiers, out int id, out int error)
        {
            _windowReadyEvent.WaitOne();
            lock (_windowReadyEvent)
            {
                id = System.Threading.Interlocked.Increment(ref _id);
                error = (int)_wnd.Invoke(new RegisterHotKeyDelegate(RegisterHotKeyInternal), _hwnd, id, (uint)modifiers, (uint)key);
                if (error != 0)
                {
                    id = 0;
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Claims a combination another application has already registered. RegisterHotKey cannot take
        /// one over, so this installs a low-level keyboard hook instead: the hook sees the key before
        /// the hot key machinery does, raises <see cref="HotKeyPressed"/> and swallows the key, so the
        /// app holding the registration never receives it. Combinations Windows reserves for itself
        /// (Ctrl+Alt+Del, Win+L) still cannot be taken.
        /// </summary>
        public static int RegisterHotKeyOverride(Keys key, KeyModifiers modifiers)
        {
            _windowReadyEvent.WaitOne();
            int id = System.Threading.Interlocked.Increment(ref _id);
            lock (_overrides)
            {
                _overrides[id] = new HotKeyEventArgs(key, modifiers);
            }
            // outside the lock: the hook callback runs on the message loop thread and takes it too
            lock (_hookLock)
            {
                if (_hook == IntPtr.Zero)
                {
                    _hook = (IntPtr)_wnd.Invoke(new Func<IntPtr>(InstallHook));
                    if (_hook == IntPtr.Zero)
                    {
                        lock (_overrides) _overrides.Remove(id);
                        throw new InvalidOperationException($"Failed to install the keyboard hook needed to take the hot key over (Windows error {_hookError}).");
                    }
                }
            }
            return id;
        }

        public static void UnregisterHotKey(int id)
        {
            bool wasOverride;
            bool anyLeft;
            lock (_overrides)
            {
                wasOverride = _overrides.Remove(id);
                anyLeft = _overrides.Count > 0;
            }
            if (wasOverride)
            {
                lock (_hookLock)
                {
                    if (!anyLeft && _hook != IntPtr.Zero)
                    {
                        _wnd.Invoke(new Action(UninstallHook));
                    }
                }
                return;
            }
            _wnd.Invoke(new UnRegisterHotKeyDelegate(UnRegisterHotKeyInternal), _hwnd, id);
        }

        delegate int RegisterHotKeyDelegate(IntPtr hwnd, int id, uint modifiers, uint key);
        delegate void UnRegisterHotKeyDelegate(IntPtr hwnd, int id);

        private static int RegisterHotKeyInternal(IntPtr hwnd, int id, uint modifiers, uint key)
        {
            // GetLastError is per thread, so the result has to be read here, on the message loop thread
            if (RegisterHotKey(hwnd, id, modifiers, key)) return 0;
            int error = Marshal.GetLastWin32Error();
            return error != 0 ? error : ERROR_HOTKEY_ALREADY_REGISTERED;
        }

        private static void UnRegisterHotKeyInternal(IntPtr hwnd, int id)
        {
            UnregisterHotKey(_hwnd, id);
        }

        private static void OnHotKeyPressed(HotKeyEventArgs e)
        {
            if (HotKeyManager.HotKeyPressed != null)
            {
                HotKeyManager.HotKeyPressed(null, e);
            }
        }

        private static volatile MessageWindow _wnd;
        private static volatile IntPtr _hwnd;
        private static ManualResetEvent _windowReadyEvent = new ManualResetEvent(false);
        static HotKeyManager()
        {
            Thread messageLoop = new Thread(delegate ()
            {
                Application.Run(new MessageWindow());
            });
            messageLoop.Name = "MessageLoopThread";
            messageLoop.SetApartmentState(ApartmentState.STA);
            messageLoop.IsBackground = true;
            messageLoop.Start();
        }

        private class MessageWindow : Form
        {
            public MessageWindow()
            {
                lock (_windowReadyEvent)
                {
                    _wnd = this;
                    _hwnd = this.Handle;
                    _windowReadyEvent.Set();
                }
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY)
                {
                    HotKeyEventArgs e = new HotKeyEventArgs(m.LParam);
                    HotKeyManager.OnHotKeyPressed(e);
                }

                base.WndProc(ref m);
            }

            protected override void SetVisibleCore(bool value)
            {
                // Ensure the window never becomes visible
                base.SetVisibleCore(false);
            }

            private const int WM_HOTKEY = 0x312;
        }

        [DllImport("user32", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private static int _id = 0;

        #region Taking a hot key over from another application

        // combinations claimed through RegisterHotKeyOverride, by registration id
        private static readonly Dictionary<int, HotKeyEventArgs> _overrides = new Dictionary<int, HotKeyEventArgs>();
        private static readonly object _hookLock = new object();
        private static IntPtr _hook = IntPtr.Zero;
        private static int _hookError;                   // last SetWindowsHookEx error, read on the hook's own thread
        private static LowLevelKeyboardProc _hookProc;   // kept alive: the hook holds a raw pointer to it
        // keys swallowed on the way down, so their key up can be swallowed too (message loop thread only)
        private static readonly HashSet<Keys> _swallowed = new HashSet<Keys>();

        /// <summary>Installs the hook. Must run on the message loop thread: that is the thread Windows calls back on.</summary>
        private static IntPtr InstallHook()
        {
            _hookProc = HookCallback;
            using (var module = System.Diagnostics.Process.GetCurrentProcess().MainModule)
            {
                IntPtr hook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(module.ModuleName), 0);
                // GetLastError belongs to this thread, so it is read here and handed back to the caller
                _hookError = hook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
                return hook;
            }
        }

        private static void UninstallHook()
        {
            if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _hookProc = null;
            _swallowed.Clear();
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                var info = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                Keys key = (Keys)info.vkCode;
                bool injected = (info.flags & LLKHF_INJECTED) != 0;   // keystrokes this app types must not trigger it

                if (!injected && (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN))
                {
                    if (_swallowed.Contains(key)) return (IntPtr)1;   // auto repeat while held: eat it, do not fire again
                    HotKeyEventArgs hotKey = MatchOverride(key);
                    if (hotKey != null)
                    {
                        _swallowed.Add(key);
                        // never do the work here: Windows drops a low-level hook that blocks for too long,
                        // and this is the hook's own thread. Handlers wait for modifier keys, touch the
                        // clipboard and open dialogs, so they get an STA thread of their own.
                        var fire = new Thread(delegate () { OnHotKeyPressed(hotKey); });
                        fire.Name = "HotKeyOverrideThread";
                        fire.SetApartmentState(ApartmentState.STA);
                        fire.IsBackground = true;
                        fire.Start();
                        return (IntPtr)1;
                    }
                }
                else if (!injected && (msg == WM_KEYUP || msg == WM_SYSKEYUP))
                {
                    if (_swallowed.Remove(key)) return (IntPtr)1;     // the other app must not see a stray key up either
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        /// <summary>The claimed hot key this key press satisfies, or null.</summary>
        private static HotKeyEventArgs MatchOverride(Keys key)
        {
            KeyModifiers held = CurrentModifiers();
            lock (_overrides)
            {
                foreach (var hotKey in _overrides.Values)
                {
                    if (hotKey.Key == key && (hotKey.Modifiers & MODIFIER_MASK) == held) return hotKey;
                }
            }
            return null;
        }

        private static KeyModifiers CurrentModifiers()
        {
            KeyModifiers held = KeyModifiers.None;
            if (IsDown(Keys.ControlKey)) held |= KeyModifiers.Control;
            if (IsDown(Keys.Menu)) held |= KeyModifiers.Alt;
            if (IsDown(Keys.ShiftKey)) held |= KeyModifiers.Shift;
            if (IsDown(Keys.LWin) || IsDown(Keys.RWin)) held |= KeyModifiers.Windows;
            return held;
        }

        private static bool IsDown(Keys key)
        {
            return (GetAsyncKeyState((int)key) & 0x8000) != 0;
        }

        private const KeyModifiers MODIFIER_MASK = KeyModifiers.Alt | KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Windows;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x100;
        private const int WM_KEYUP = 0x101;
        private const int WM_SYSKEYDOWN = 0x104;
        private const int WM_SYSKEYUP = 0x105;
        private const uint LLKHF_INJECTED = 0x10;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32", SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        #endregion
    }


    public class HotKeyEventArgs : EventArgs
    {
        public readonly Keys Key;
        public readonly KeyModifiers Modifiers;

        public HotKeyEventArgs(Keys key, KeyModifiers modifiers)
        {
            this.Key = key;
            this.Modifiers = modifiers;
        }

        public HotKeyEventArgs(IntPtr hotKeyParam)
        {
            uint param = (uint)hotKeyParam.ToInt64();
            Key = (Keys)((param & 0xffff0000) >> 16);
            Modifiers = (KeyModifiers)(param & 0x0000ffff);
        }
    }

    [Flags]
    public enum KeyModifiers
    {
        None = 0,
        Alt = 1,
        Control = 2,
        Shift = 4,
        Windows = 8,
        NoRepeat = 0x4000
    }
}
