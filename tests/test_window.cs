// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// Render the installer's small window offscreen; file operations are covered by test_installer.py.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
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
                if (window.Controls.Find("LanguageBox", true).Length != 1) throw new InvalidOperationException("Language selector is missing");
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
                {   // a status line and new info rows (after a netplay download) must not bring up a horizontal scroll bar
                    var say = window.GetType().GetMethod("Say", BindingFlags.NonPublic | BindingFlags.Instance);
                    say.Invoke(window, new object[] { "Netplay installed from oldkid.net: wsock32.dll, netcheck.exe, okplay.ini and oldkid_netplay_readme.txt are in the game folder." });
                    for (int i = 0; i < 5; ++i) Application.DoEvents();
                    var scrollPanel = (ScrollableControl)window.Controls.Find("ScrollPanel", true)[0];
                    if (scrollPanel.HorizontalScroll.Visible) {
                        var wide = new System.Text.StringBuilder();
                        Action<Control, int> walk = null;
                        walk = (c, depth) => { foreach (Control k in c.Controls) { int right = k.PointToScreen(new Point(k.Width, 0)).X - window.PointToScreen(Point.Empty).X; if (right > window.ClientSize.Width - 20 && k.Width > 60) wide.Append(k.Name + "/" + k.GetType().Name + "=" + k.Width + "@" + right + "; "); walk(k, depth + 1); } };
                        walk(window, 0);
                        throw new InvalidOperationException("WIDE: " + wide);
                    }
                    if (scrollPanel.HorizontalScroll.Visible) throw new InvalidOperationException("A status line must not bring up a horizontal scroll bar (window " + window.Width + "x" + window.Height + ", client " + window.ClientSize + ", min " + scrollPanel.AutoScrollMinSize + ", compact " + window.GetType().GetField("compact", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window) + ", area " + Screen.FromPoint(Cursor.Position).WorkingArea + ")");
                }
                // One margin: the content blocks and the button bar start at the same left edge, links and the logo click are wired.
                {
                    var rootPanel = window.Controls.Find("RootPanel", true)[0];
                    int edge = rootPanel.Padding.Left;
                    foreach (string name in new[] { "HeadTable", "OptionsLabel", "OptionsTable", "FooterTable" })
                    {
                        var c = window.Controls.Find(name, true);
                        if (c.Length != 1) throw new InvalidOperationException("Missing control " + name);
                        if (c[0].Left != edge) throw new InvalidOperationException(name + " must start at the common margin (" + edge + "), starts at " + c[0].Left);
                    }
                    {   // the credits sit in the columns of the options: names under the option names, text under the descriptions
                        var line = window.Controls.Find("VideoVolumeLine", true)[0]; 
                        var info = window.Controls.Find("InstallPanel", true)[0];
                        bool oneColumn = (bool)window.GetType().GetField("compact", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);   // narrow screen: the options are one column
                        if (!oneColumn && info.PointToScreen(Point.Empty).X != line.PointToScreen(Point.Empty).X) throw new InvalidOperationException("The installations block must start on the description column (" + info.PointToScreen(Point.Empty).X + " vs " + line.PointToScreen(Point.Empty).X + ")");
                    }
                    var bar0 = window.Controls.Find("ButtonBar", true)[0]; var buttons0 = window.Controls.Find("PlayButton", true)[0].Parent;
                    if (buttons0.Left != bar0.Padding.Left || bar0.Padding.Left != edge) throw new InvalidOperationException("The buttons must use the same margin as the content (buttons " + buttons0.Left + ", bar padding " + bar0.Padding.Left + ", content " + edge + ")");
                    var logoBox = window.Controls.Find("LogoBox", true);
                    if (logoBox.Length != 1 || logoBox[0].Cursor != Cursors.Hand) throw new InvalidOperationException("The logo must be a clickable link");
                    // one font everywhere (the bold Play button is the only exception)
                    foreach (Control c in AllControls(window))
                    {
                        if (c is TrackBar || c is PictureBox || c is Panel && !(c is FlowLayoutPanel) && !(c is TableLayoutPanel)) continue;
                        if (c.Name == "PlayButton") continue;
                        if (c.Font.FontFamily.Name != window.Font.FontFamily.Name || c.Font.Size != window.Font.Size || c.Font.Style != FontStyle.Regular)
                            throw new InvalidOperationException("Control " + c.Name + " uses another font: " + c.Font);
                    }
                    // Netplay: a missing wsock32.dll gives a link to OKPlay, a present one shows its datecode
                    string gameFolder = Path.Combine(Path.GetTempPath(), "rrmf_netplay_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(gameFolder);
                    try
                    {
                        File.WriteAllBytes(Path.Combine(gameFolder, "RoadRash95.exe"), new byte[] { 0x4d, 0x5a });
                        var pathBox = (TextBox)window.Controls.Find("FolderBox", true)[0];
                        pathBox.Text = gameFolder;
                        Application.DoEvents();
                        if (window.Controls.Find("NetplayLink", true).Length != 1) throw new InvalidOperationException("A missing wsock32.dll must show the OKPlay link");
                        File.WriteAllBytes(Path.Combine(gameFolder, "wsock32.dll"), new byte[] { 0x4d, 0x5a });
                        pathBox.Text = ""; pathBox.Text = gameFolder;
                        Application.DoEvents();
                        var netplay = window.Controls.Find("NetplayLabel", true);
                        if (netplay.Length != 1 || !netplay[0].Text.StartsWith("wsock32.dll present (datecode: ")) throw new InvalidOperationException("A present wsock32.dll must show its datecode");
                        if (window.Controls.Find("NetplayLink", true).Length != 0) throw new InvalidOperationException("The OKPlay link must disappear when wsock32.dll is there");
                    }
                    finally { try { Directory.Delete(gameFolder, true); } catch (Exception) { } }
                    // "iGPU Performance Boost" is the inverse of [Performance] LowLatency: checked = 0
                    {
                        var optionsField = assembly.GetType("Installer").GetField("Options", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                        var all = (System.Collections.IEnumerable)optionsField.GetValue(null);
                        object boost = null;
                        foreach (object o in all) if ((string)o.GetType().GetField("Key").GetValue(o) == "LowLatency") boost = o;
                        if (boost == null) throw new InvalidOperationException("The iGPU Performance Boost option is missing");
                        if ((string)boost.GetType().GetField("Label").GetValue(boost) != "iGPU Performance Boost" || (string)boost.GetType().GetField("Hint").GetValue(boost) != "Turns off Low-Latency Presentation: faster on battery-powered Intel iGPUs")
                            throw new InvalidOperationException("The battery boost option must have the short label and hint");
                        if ((string)boost.GetType().GetField("Label").GetValue(boost) == "" || ((string)boost.GetType().GetField("Hint").GetValue(boost)).Contains("Experimental")) throw new InvalidOperationException("No 'Experimental' in the option text");
                        string iniFolder = Path.Combine(Path.GetTempPath(), "rrmf_boost_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(iniFolder);
                        try
                        {
                            string iniFile = Path.Combine(iniFolder, "RoadRashHD.ini"); File.WriteAllText(iniFile, "[Performance]" + Environment.NewLine);
                            var save = assembly.GetType("Installer").GetMethod("SaveOption", BindingFlags.Public | BindingFlags.Static);
                            var read = assembly.GetType("Installer").GetMethod("ReadOption", BindingFlags.Public | BindingFlags.Static);
                            save.Invoke(null, new object[] { iniFolder, boost, true });
                            if (!File.ReadAllText(iniFile).Contains("LowLatency=0")) throw new InvalidOperationException("Checked must write LowLatency=0");
                            if (!(bool)read.Invoke(null, new object[] { iniFolder, boost })) throw new InvalidOperationException("LowLatency=0 must read back as checked");
                            save.Invoke(null, new object[] { iniFolder, boost, false });
                            if (!File.ReadAllText(iniFile).Contains("LowLatency=1")) throw new InvalidOperationException("Unchecked must write LowLatency=1");
                            if ((bool)read.Invoke(null, new object[] { iniFolder, boost })) throw new InvalidOperationException("LowLatency=1 must read back as unchecked");
                        }
                        finally { try { Directory.Delete(iniFolder, true); } catch (Exception) { } }
                    }
                    // netplay: unpack the zip (only the four files, readme renamed, existing okplay.ini kept) and download with a fallback
                    {
                        var inst = assembly.GetType("Installer");
                        var extract = inst.GetMethod("ExtractNetplay", BindingFlags.Public | BindingFlags.Static);
                        Func<bool, bool, byte[]> makeZip = (withDll, badDll) =>
                        {
                            using (var ms = new MemoryStream())
                            {
                                using (var za = new ZipArchive(ms, ZipArchiveMode.Create, true))
                                {
                                    Action<string, byte[]> add = (name, data) => { var e = za.CreateEntry(name); using (var s = e.Open()) s.Write(data, 0, data.Length); };
                                    byte[] mz = new byte[] { 0x4d, 0x5a, 1, 2, 3 };
                                    if (withDll) add("Oldkid.net-Online-Play-v0.8.5/wsock32.dll", badDll ? new byte[] { 1, 2, 3, 4 } : mz);
                                    add("Oldkid.net-Online-Play-v0.8.5/netcheck.exe", mz);
                                    add("okplay.ini", Encoding.ASCII.GetBytes("[net]"));
                                    add("readme.txt", Encoding.ASCII.GetBytes("hello"));
                                    add("other.txt", Encoding.ASCII.GetBytes("not wanted"));
                                    add("../evil.dll", mz);
                                }
                                return ms.ToArray();
                            }
                        };
                        string root1 = Path.Combine(Path.GetTempPath(), "rrmf_np_" + Guid.NewGuid().ToString("N")), game1 = Path.Combine(root1, "game");
                        Directory.CreateDirectory(game1);
                        try
                        {
                            extract.Invoke(null, new object[] { makeZip(true, false), game1 });
                            foreach (string f in new[] { "wsock32.dll", "netcheck.exe", "okplay.ini", "oldkid_netplay_readme.txt" })
                                if (!File.Exists(Path.Combine(game1, f))) throw new InvalidOperationException("Netplay file missing after unpacking: " + f);
                            if (File.Exists(Path.Combine(game1, "readme.txt")) || File.Exists(Path.Combine(game1, "other.txt")) || File.Exists(Path.Combine(root1, "evil.dll")) || File.Exists(Path.Combine(game1, "evil.dll")))
                                throw new InvalidOperationException("Only the four netplay files may be written");
                            File.WriteAllText(Path.Combine(game1, "okplay.ini"), "mine");
                            extract.Invoke(null, new object[] { makeZip(true, false), game1 });
                            if (File.ReadAllText(Path.Combine(game1, "okplay.ini")) != "mine") throw new InvalidOperationException("An existing okplay.ini must be kept");
                            try { extract.Invoke(null, new object[] { makeZip(false, false), game1 }); throw new InvalidOperationException("A zip without wsock32.dll must be rejected"); }
                            catch (TargetInvocationException) { }
                            try { extract.Invoke(null, new object[] { makeZip(true, true), game1 }); throw new InvalidOperationException("A wsock32.dll that is not a Windows program must be rejected"); }
                            catch (TargetInvocationException) { }

                            // download: first address fails (500), second redirects to the zip
                            byte[] zipBytes = makeZip(true, false);
                            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
                            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                            bool stop = false;
                            var server = new Thread(() =>
                            {
                                while (!stop)
                                {
                                    TcpClient client;
                                    try { client = listener.AcceptTcpClient(); } catch (Exception) { return; }
                                    using (client)
                                    {
                                        var ns = client.GetStream(); var buf = new byte[4096]; int n = ns.Read(buf, 0, buf.Length);
                                        string request = Encoding.ASCII.GetString(buf, 0, n); string line = request.Split(new[] { "\r\n" }, StringSplitOptions.None)[0];
                                        byte[] head; byte[] body = new byte[0];
                                        if (line.Contains("/bad")) head = Encoding.ASCII.GetBytes("HTTP/1.1 500 Server Error\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                                        else if (line.Contains("/latest")) head = Encoding.ASCII.GetBytes("HTTP/1.1 302 Found\r\nLocation: pack.zip\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                                        else { body = zipBytes; head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/zip\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n"); }
                                        ns.Write(head, 0, head.Length); ns.Write(body, 0, body.Length);
                                    }
                                }
                            });
                            server.IsBackground = true; server.Start();
                            try
                            {
                                var urls = inst.GetField("NetplayUrls", BindingFlags.NonPublic | BindingFlags.Static);
                                var download = inst.GetMethod("DownloadNetplay", BindingFlags.Public | BindingFlags.Static);
                                string game2 = Path.Combine(root1, "game2"); Directory.CreateDirectory(game2); File.WriteAllBytes(Path.Combine(game2, "RoadRash95.exe"), new byte[] { 0x4d, 0x5a });
                                urls.SetValue(null, new[] { "http://127.0.0.1:" + port + "/bad", "http://127.0.0.1:" + port + "/latest" });
                                object[] args3 = { game2, null };
                                string source = (string)download.Invoke(null, args3);
                                if (source == null || !File.Exists(Path.Combine(game2, "wsock32.dll")) || !File.Exists(Path.Combine(game2, "oldkid_netplay_readme.txt")))
                                    throw new InvalidOperationException("The download must fall through to the second address (error: " + args3[1] + ")");
                                string game3 = Path.Combine(root1, "game3"); Directory.CreateDirectory(game3); File.WriteAllBytes(Path.Combine(game3, "RoadRash95.exe"), new byte[] { 0x4d, 0x5a });
                                urls.SetValue(null, new[] { "http://127.0.0.1:" + port + "/bad" });
                                object[] args4 = { game3, null };
                                if (download.Invoke(null, args4) != null || File.Exists(Path.Combine(game3, "wsock32.dll")) || string.IsNullOrEmpty((string)args4[1]))
                                    throw new InvalidOperationException("When every address fails nothing is written and an error is reported");
                            }
                            finally { stop = true; try { listener.Stop(); } catch (Exception) { } }
                        }
                        finally { try { Directory.Delete(root1, true); } catch (Exception) { } }
                    }
                    // release check helpers
                    var installerType = assembly.GetType("Installer");
                    var isNewer = installerType.GetMethod("IsNewer", BindingFlags.Public | BindingFlags.Static);
                    Func<string, string, bool> newer = (a, b) => (bool)isNewer.Invoke(null, new object[] { a, b });
                    if (!newer("2.16.0", "2.15.0") || !newer("v3.0", "2.99.9") || !newer("2.15.1", "2.15.0")) throw new InvalidOperationException("IsNewer must recognise a newer release");
                    if (newer("2.15.0", "2.15.0") || newer("2.14.9", "2.15.0") || newer("garbage", "2.15.0")) throw new InvalidOperationException("IsNewer must not flag the same, older or invalid versions");
                    var parse = installerType.GetMethod("ParseLatestRelease", BindingFlags.Public | BindingFlags.Static);
                    object[] args2 = { "{\"tag_name\":\"v2.16.0\",\"html_url\":\"https://github.com/magoowa/RoadRash30-MultiFix/releases/tag/v2.16.0\"}", null, null };
                    if (!(bool)parse.Invoke(null, args2) || (string)args2[1] != "2.16.0" || !((string)args2[2]).EndsWith("/releases/tag/v2.16.0")) throw new InvalidOperationException("The GitHub release JSON must be parsed");
                    object[] bad = { "{\"message\":\"Not Found\"}", null, null };
                    if ((bool)parse.Invoke(null, bad)) throw new InvalidOperationException("A response without a version must be rejected");
                }
                // Small screen: the window never exceeds the working area, the content scrolls, the buttons stay in view.
                {
                    var field = assembly.GetType("PatchWindow").GetField("WorkingAreaOverride", BindingFlags.NonPublic | BindingFlags.Static);
                    field.SetValue(null, new Rectangle?(new Rectangle(0, 0, 800, 480)));
                    using (var small = (Form)factory.Invoke(null, null))
                    {
                        small.Location = new Point(-30000, -30000);
                        small.ShowInTaskbar = false;
                        var smallHandle = small.Handle;
                        SetWindowLong(smallHandle, -20, GetWindowLong(smallHandle, -20) | 0x08000000);
                        small.Show();
                        for (int i = 0; i < 5; ++i) Application.DoEvents();
                        if (small.Height > 480 || small.Width > 800) throw new InvalidOperationException("The window must fit a 800x480 working area, got " + small.Width + "x" + small.Height + " (client " + small.ClientSize + ", min " + small.MinimumSize + ", border " + small.FormBorderStyle + ")");
                        var scroller = (ScrollableControl)small.Controls.Find("ScrollPanel", true)[0];
                        var bar = small.Controls.Find("ButtonBar", true)[0];
                        if (!scroller.VerticalScroll.Visible) throw new InvalidOperationException("A vertical scroll bar is expected on a small screen");
                        if (scroller.HorizontalScroll.Visible) {
                            var wide = new System.Text.StringBuilder();
                            Action<Control> walk = null;
                            walk = c => { foreach (Control k in c.Controls) { int right = k.PointToScreen(new Point(k.Width, 0)).X - small.PointToScreen(Point.Empty).X; if (right > small.ClientSize.Width - 30 && k.Width > 40) wide.Append(k.Name + "/" + k.GetType().Name + "=" + k.Width + "@" + right + "; "); walk(k); } };
                            walk(small);
                            var rootPanel = small.Controls.Find("RootPanel", true)[0];
                            foreach (Control k in rootPanel.Controls) wide.Append("PREF " + k.Name + "/" + k.GetType().Name + "=" + k.GetPreferredSize(Size.Empty).Width + "; ");
                            var headT = small.Controls.Find("HeadTable", true)[0];
                            foreach (Control k in headT.Controls) wide.Append("HEAD " + k.Name + "=" + k.GetPreferredSize(Size.Empty).Width + "; ");
                            var instP = small.Controls.Find("InstallPanel", true)[0];
                            foreach (Control k in instP.Controls) wide.Append("INST " + k.Name + "=" + k.GetPreferredSize(Size.Empty).Width + "; ");
                            throw new InvalidOperationException("A narrow screen must reflow to one column, not scroll sideways: client " + small.ClientSize + ", min " + scroller.AutoScrollMinSize + ", " + wide);
                        }
                        string lowResShot = Environment.GetEnvironmentVariable("RRMF_LOWRES_SHOT");
                        if (!string.IsNullOrEmpty(lowResShot))
                            using (var shot = new Bitmap(small.Width, small.Height)) { small.DrawToBitmap(shot, new Rectangle(Point.Empty, small.Size)); shot.Save(lowResShot, ImageFormat.Png); }
                        var playButton = (Control)small.Controls.Find("PlayButton", true)[0];
                        if (bar.Bottom > small.ClientSize.Height || !playButton.Visible || playButton.Bottom > bar.ClientSize.Height || playButton.Top < 0) throw new InvalidOperationException("The buttons must stay fully in view in the button bar");
                        var last = small.Controls.Find("ContributionsLabel", true)[0];
                        scroller.VerticalScroll.Value = scroller.VerticalScroll.Maximum;
                        scroller.PerformLayout();
                        var bottomLeft = scroller.PointToClient(last.PointToScreen(new Point(0, last.Height - 1)));
                        if (bottomLeft.Y > scroller.ClientSize.Height) throw new InvalidOperationException("Scrolling down must reveal the end of the content");
                    }
                    field.SetValue(null, null);
                }
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
