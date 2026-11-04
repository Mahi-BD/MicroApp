using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// Double-click a .ma project in File Explorer → it opens in the image editor.
    /// Registers the file type for the current user, and hands a file to the MicroApp
    /// that is already running (only one instance runs) over a named pipe.
    /// </summary>
    static class FileOpen
    {
        const string ProgId = "MicroApp.Project";

        static string PipeName(string appGuid) { return "MicroApp-open-" + appGuid; }

        /// <summary>The existing files named on the command line (Explorer passes "%1").</summary>
        public static string[] FilesFromArgs(string[] args)
        {
            if (args == null) return new string[0];
            return args.Where(a => !string.IsNullOrWhiteSpace(a) && !a.StartsWith("--"))
                       .Select(a => a.Trim('"'))
                       .Where(File.Exists)
                       .Select(Path.GetFullPath)
                       .ToArray();
        }

        /// <summary>Second instance: give the files to the running one. True when it took them.</summary>
        public static bool SendToRunning(string appGuid, string[] files)
        {
            // let the running instance bring its editor window to the front
            AllowSetForegroundWindow(-1);
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    using (var pipe = new NamedPipeClientStream(".", PipeName(appGuid), PipeDirection.Out))
                    {
                        pipe.Connect(1000);
                        byte[] data = Encoding.UTF8.GetBytes(string.Join("\n", files));
                        pipe.Write(data, 0, data.Length);
                        pipe.Flush();
                        return true;
                    }
                }
                catch (Exception)
                {
                    Thread.Sleep(300);   // the first instance may still be starting up
                }
            }
            return false;
        }

        /// <summary>First instance: listen for files sent by later launches; <paramref name="open"/> runs on the UI thread.</summary>
        public static void Listen(string appGuid, Control sync, Action<string[]> open)
        {
            var thread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using (var pipe = new NamedPipeServerStream(PipeName(appGuid), PipeDirection.In, 1))
                        {
                            pipe.WaitForConnection();
                            using (var reader = new StreamReader(pipe, Encoding.UTF8))
                            {
                                string[] files = reader.ReadToEnd()
                                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Where(File.Exists).ToArray();
                                if (files.Length > 0 && sync.IsHandleCreated)
                                    sync.BeginInvoke(new Action(() => open(files)));
                            }
                        }
                    }
                    catch (Exception)
                    {
                        Thread.Sleep(1000);
                    }
                }
            });
            thread.IsBackground = true;
            thread.Name = "MicroApp file-open pipe";
            thread.Start();
        }

        /// <summary>
        /// Points .ma at this exe for the current user (no admin needed). Rewritten only when
        /// it is missing or points somewhere else, e.g. after MicroApp was moved.
        /// The Store (MSIX) build declares the association in its manifest instead.
        /// </summary>
        public static void RegisterFileType()
        {
            try
            {
                string exe = Application.ExecutablePath;
                if (exe.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) >= 0) return;

                string command = "\"" + exe + "\" \"%1\"";
                using (var existing = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + ProgId + @"\shell\open\command"))
                using (var ext = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + ProjectFile.Extension))
                {
                    if (existing != null && ext != null &&
                        string.Equals(existing.GetValue("") as string, command, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(ext.GetValue("") as string, ProgId, StringComparison.Ordinal))
                        return;
                }

                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId))
                {
                    key.SetValue("", "MicroApp Project");
                    key.SetValue("FriendlyTypeName", "MicroApp Project");
                }
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId + @"\DefaultIcon"))
                    key.SetValue("", "\"" + exe + "\",0");
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId + @"\shell\open"))
                    key.SetValue("", "Open in MicroApp Image Editor");
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId + @"\shell\open\command"))
                    key.SetValue("", command);
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProjectFile.Extension))
                {
                    key.SetValue("", ProgId);
                    key.SetValue("Content Type", "application/x-microapp-project");
                }
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProjectFile.Extension + @"\OpenWithProgids"))
                    key.SetValue(ProgId, new byte[0], RegistryValueKind.None);

                SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception) { }
        }

        [DllImport("user32.dll")]
        static extern bool AllowSetForegroundWindow(int processId);

        [DllImport("shell32.dll")]
        static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
    }
}
