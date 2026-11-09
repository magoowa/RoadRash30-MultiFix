// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// Render the installer's small window offscreen; file operations are covered by test_installer.py.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Forms;

internal static class WindowTest
{
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr window, int index, int value);
    static string Hash(string path)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
    }
    static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control c in root.Controls) { yield return c; foreach (var d in AllControls(c)) yield return d; }
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Expected installer path and output PNG");
            string installer = Path.GetFullPath(args[0]), root = Path.GetDirectoryName(installer);
            var before = new Dictionary<string, string>();
            foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) before.Add(path, Hash(path));
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var assembly = Assembly.LoadFrom(installer);
            var factory = assembly.GetType("Installer").GetMethod("CreateWindow", BindingFlags.NonPublic | BindingFlags.Static);
            using (var window = (Form)factory.Invoke(null, null))
            {
                var installButton = window.Controls.Find("InstallButton", true);
                var restoreButton = window.Controls.Find("RestoreButton", true);
                var folderBox = window.Controls.Find("FolderBox", true);
                if (installButton.Length != 1 || restoreButton.Length != 1 || folderBox.Length != 1 ||
                    installButton[0].Text != "Install / Update" || restoreButton[0].Text != "Restore original")
                    throw new InvalidOperationException("Window must offer Install / Update, Restore original and a game folder box");
                if (window.Icon == null) throw new InvalidOperationException("Installer icon is missing");
                if (window.Controls.Find("PlayButton", true).Length != 1) throw new InvalidOperationException("Play button is missing");
                int options = 0;
                foreach (Control c in AllControls(window)) if (c.Name.StartsWith("Option_")) ++options;
                if (options < 10) throw new InvalidOperationException("Expected one row per option, found " + options);
                window.StartPosition = FormStartPosition.Manual;
                window.Location = new Point(-30000, -30000);
                window.ShowInTaskbar = false;
                var handle = window.Handle;
                SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x08000000); // WS_EX_NOACTIVATE
                window.Show();
                Application.DoEvents();
                if (Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length != before.Count)
                    throw new InvalidOperationException("Opening the window created files");
                foreach (var file in before)
                    if (Hash(file.Key) != file.Value) throw new InvalidOperationException("Opening changed " + file.Key);
                // Live ini behaviour: toggling a box writes the ini at once; an external edit is read back when the window is activated.
                string iniPath = Path.Combine(root, "RoadRashHD.ini");
                File.WriteAllText(iniPath, "[Graphics]\r\nTextureFilter=1\r\n");
                var folderText = window.Controls.Find("FolderBox", true)[0];
                folderText.Text = "";
                folderText.Text = root;
                var filter = (CheckBox)window.Controls.Find("Option_Graphics_TextureFilter", true)[0];
                if (!filter.Checked) throw new InvalidOperationException("The window did not read TextureFilter=1 from the ini");
                filter.Checked = false;
                if (!File.ReadAllText(iniPath).Contains("TextureFilter=0")) throw new InvalidOperationException("Unchecking a box did not update the ini immediately");
                File.WriteAllText(iniPath, "[Graphics]\r\nTextureFilter=1\r\n");
                typeof(Form).GetMethod("OnActivated", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, new object[] { EventArgs.Empty });
                if (!filter.Checked) throw new InvalidOperationException("An external ini change was not picked up on activation");
                File.Delete(iniPath);
                using (var bitmap = new Bitmap(window.Width, window.Height))
                {
                    window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size));
                    bitmap.Save(args[1], ImageFormat.Png);
                }
                ((Button)window.Controls.Find("CloseButton", true)[0]).PerformClick();
                if (!window.IsDisposed) throw new InvalidOperationException("The Close button did not close the window");
            }
            // Installation / DLL version detection in a throw-away game folder.
            {
                string game = Path.Combine(Path.GetTempPath(), "rrmf_status_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(game);
                try
                {
                    File.WriteAllBytes(Path.Combine(game, "RoadRash95.exe"), new byte[] { 0x4d, 0x5a });
                    var type = assembly.GetType("Installer");
                    var status = type.GetMethod("Status", BindingFlags.Public | BindingFlags.Static);
                    var execute = type.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static);
                    Func<object> now = () => status.Invoke(null, new object[] { game });
                    Func<string> describe = () => (string)now().GetType().GetMethod("Describe").Invoke(now(), null);
                    if (!describe().StartsWith("NOT installed")) throw new InvalidOperationException("Empty folder must report NOT installed, got: " + describe());
                    var run = execute.Invoke(null, new object[] { false, game, null });
                    if (!(bool)run.GetType().GetField("Success").GetValue(run)) throw new InvalidOperationException("Test install failed: " + run.GetType().GetField("Message").GetValue(run));
                    if (!describe().Contains("up to date") || !describe().Contains("DLL version")) throw new InvalidOperationException("Installed folder must report the DLL version, got: " + describe());
                    File.WriteAllBytes(Path.Combine(game, "RoadRashMultiFix.dll"), new byte[] { 1, 2, 3 });
                    if (!describe().Contains("press Install / Update")) throw new InvalidOperationException("A different DLL must be reported as outdated, got: " + describe());
                    File.Delete(Path.Combine(game, "RoadRashMultiFix.dll"));
                    if (!describe().StartsWith("partially installed")) throw new InvalidOperationException("A missing DLL must be reported, got: " + describe());
                    execute.Invoke(null, new object[] { false, game, null });
                    execute.Invoke(null, new object[] { true, game, null });
                    if (!describe().StartsWith("NOT installed")) throw new InvalidOperationException("After Restore the folder must report NOT installed, got: " + describe());
                }
                finally { try { Directory.Delete(game, true); } catch (Exception) { } }
            }
            Console.WriteLine("GUI: installer window shows the folder, options and Install/Restore; opening does not change files");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
