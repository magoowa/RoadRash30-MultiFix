// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

internal sealed class PatchResult
{
    public readonly bool Success;
    public readonly string Message;
    public PatchResult(bool success, string message) { Success = success; Message = message; }
}

// Installer + launcher window. One font (Segoe UI 9) for everything, one margin unit: every block is inset by the same
// margin and separated by the same gaps, so all left edges line up. The layout is built from tables that size
// themselves from the font, so it stays correct at any display scale (100 % - 300 %). Every option has its own row
// and is read from / written to RoadRashHD.ini as it changes.
internal sealed class PatchWindow : Form
{
    readonly ComboBox installs = new ComboBox();
    readonly TextBox path = new TextBox();                           // the chosen game folder (state holder, not shown)
    readonly Button browse = new Button(), play = new Button(), install = new Button(), restore = new Button();
    readonly TableLayoutPanel infoTable = new TableLayoutPanel();
    readonly Label status = new Label();
    readonly List<CheckBox> boxes = new List<CheckBox>();
    readonly ToolTip tips = new ToolTip();
    readonly TrackBar videoVolume = new TrackBar();                  // [Game] VideoVolume
    readonly Label videoVolumeText = new Label();
    readonly FlowLayoutPanel languageRow = new FlowLayoutPanel();   // one radio button per language
    bool loading;
    string latestVersion, latestUrl;                                 // set when GitHub has a newer release
    bool netplayBusy;string netplayError;                            // netplay download state

    // All sizes derive from the font height, which already follows the display scale (100 % ... 300 %).
    int Px(int value) { return (int)Math.Round(value * Font.Height / 15.0); }
    // The one margin unit of the window, and the small gap between rows.
    int Margin12 { get { return Px(12); } }
    int RowGap { get { return Px(2); } }

    public PatchWindow() : this(null) { }

    public PatchWindow(string initialFolder)
    {
        Text = Installer.Title;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Font;
        // The content scrolls: on a small screen (low resolution, big display scale) the window is as large as the
        // screen allows and gets scroll bars; the buttons stay visible at the bottom. It can also be resized/maximized.
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true; MinimizeBox = false;
        StartPosition = FormStartPosition.Manual;
        try
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                if (stream != null) Icon = new Icon(stream);
        }
        catch (Exception) { }

        int m = Margin12, rg = RowGap, wrap = Px(560);
        var root = new TableLayoutPanel
        {
            Name = "RootPanel", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Top,
            Padding = new Padding(m), MinimumSize = new Size(Px(700), 0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        // Top block: the logo on the left (a click opens the project page), installations and info on the right.
        var head = new TableLayoutPanel { Name = "HeadTable", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, m) };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        try
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png"))
                if (stream != null)
                {
                    var copy = new System.IO.MemoryStream();
                    stream.CopyTo(copy); copy.Position = 0;
                    var image = Image.FromStream(copy);
                    int width = Px(170), height = width * image.Height / image.Width;
                    var logo = new PictureBox { Name = "LogoBox", Image = image, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(width, height), Margin = new Padding(0, 0, m, 0), Anchor = AnchorStyles.Top | AnchorStyles.Left, Cursor = Cursors.Hand };
                    tips.SetToolTip(logo, Installer.ProjectUrl);
                    logo.Click += delegate { OpenUrl(Installer.ProjectUrl); };
                    head.Controls.Add(logo, 0, 0);
                }
        }
        catch (Exception) { }
        var right = new TableLayoutPanel { Name = "InstallPanel", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0) };
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); right.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var installsLabel = new Label { Text = "Detected installations", AutoSize = true, Margin = new Padding(0, 0, 0, rg) };
        right.Controls.Add(installsLabel, 0, 0); right.SetColumnSpan(installsLabel, 2);
        installs.Name = "InstallsList"; installs.DropDownStyle = ComboBoxStyle.DropDownList; installs.Dock = DockStyle.Fill; installs.Margin = new Padding(0, 0, rg * 3, m / 2);
        browse.Name = "BrowseButton"; browse.Text = "Browse..."; browse.AutoSize = true; browse.Margin = new Padding(0, 0, 0, m / 2);
        right.Controls.Add(installs, 0, 1); right.Controls.Add(browse, 1, 1);
        // The info lines (Executable / MultiFix / Language / Netplay) are filled in by RefreshInfo.
        infoTable.Name = "InfoBox"; infoTable.AutoSize = true; infoTable.AutoSizeMode = AutoSizeMode.GrowAndShrink; infoTable.ColumnCount = 2; infoTable.Dock = DockStyle.Fill; infoTable.Margin = new Padding(0);
        infoTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); infoTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        right.Controls.Add(infoTable, 0, 2); right.SetColumnSpan(infoTable, 2);
        head.Controls.Add(right, 1, 0);
        root.Controls.Add(head);
        // The folder is shown in the installations list; the text box only holds it (it is not displayed).
        path.Name = "FolderBox"; path.Visible = false; Controls.Add(path);

        languageRow.Name = "LanguageBox"; languageRow.AutoSize = true; languageRow.AutoSizeMode = AutoSizeMode.GrowAndShrink; languageRow.FlowDirection = FlowDirection.LeftToRight;
        languageRow.WrapContents = false; languageRow.Margin = new Padding(0, 0, 0, rg);
        tips.SetToolTip(languageRow, "Game/Language - game text language (the DLLs in the TEXT folder); Auto = the game's own setting");

        root.Controls.Add(new Label { Name = "OptionsLabel", Text = "Options (live from RoadRashHD.ini)", AutoSize = true, Margin = new Padding(0, 0, 0, m / 2) });
        var rows = new TableLayoutPanel { Name = "OptionsTable", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, m) };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        int r = 0;
        foreach (var option in Installer.Options)
        {
            var box = new CheckBox { Text = option.Label, Tag = option, AutoSize = true, Checked = option.Default, Anchor = AnchorStyles.Left, Margin = new Padding(0, rg, m, rg) };
            box.Name = "Option_" + option.Section + "_" + option.Key;
            var hint = new Label { Text = option.Hint, AutoSize = true, ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Left, Margin = new Padding(0, rg, 0, rg) };
            tips.SetToolTip(box, option.Section + "/" + option.Key + " - " + option.Hint);
            box.CheckedChanged += OptionChanged;
            boxes.Add(box);
            rows.Controls.Add(box, 0, r); rows.Controls.Add(hint, 1, r); ++r;
        }
        // FMV volume as in the design: the name under the option names (lined up with their text), the slider and its value
        // in the column of the descriptions, the description on the next line.
        {
            var label = new Label { Text = "FMV Volume", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(Px(13), rg, m, rg) };
            videoVolume.Name = "VideoVolumeBar"; videoVolume.Minimum = 0; videoVolume.Maximum = 100; videoVolume.TickFrequency = 10; videoVolume.SmallChange = 5; videoVolume.LargeChange = 10;
            videoVolume.Value = 100; videoVolume.AutoSize = false; videoVolume.Size = new Size(Px(180), Px(26)); videoVolume.Margin = new Padding(0, 0, m, 0);
            videoVolumeText.Name = "VideoVolumeText"; videoVolumeText.Text = "100 %"; videoVolumeText.AutoSize = true; videoVolumeText.Anchor = AnchorStyles.Left; videoVolumeText.MinimumSize = new Size(Px(44), 0);
            var line = new FlowLayoutPanel { Name = "VideoVolumeLine", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
            line.Controls.Add(videoVolume); line.Controls.Add(videoVolumeText);
            var hint = new Label { Text = "Volume of the intro and story videos (.avi and .mp4)", AutoSize = true, ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Left, Margin = new Padding(0, rg, 0, rg) };
            tips.SetToolTip(videoVolume, "Game/VideoVolume - volume of all videos, 0-100 % of the game's volume");
            videoVolume.ValueChanged += VideoVolumeChanged;
            rows.Controls.Add(label, 0, r); rows.Controls.Add(line, 1, r); ++r;
            rows.Controls.Add(hint, 1, r); ++r;
        }
        root.Controls.Add(rows);

        status.Name = "StatusLabel"; status.Visible = false; status.AutoSize = true; status.MaximumSize = new Size(wrap, 0); status.ForeColor = SystemColors.GrayText; status.Margin = new Padding(0, 0, 0, m);
        root.Controls.Add(status);

        // The about text on the left, the credits on the right (same row, so the window stays short).
        var footer = new TableLayoutPanel { Name = "FooterTable", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, m) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(new Label { Name = "AboutLabel", Text = "Road Rash™: 30th Anniversary Edition - fixes, widescreen and a Direct3D renderer. MultiFix loads through ddraw.dll; RoadRash95.exe is never modified.",
            AutoSize = true, MaximumSize = new Size(wrap * 2 / 5, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, m * 3, 0), Anchor = AnchorStyles.Top | AnchorStyles.Left }, 0, 0);

        // Credits: two columns, heading above the names.
        var credits = new TableLayoutPanel { Name = "AuthorsTable", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Anchor = AnchorStyles.Top | AnchorStyles.Left, Margin = new Padding(0) };
        credits.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); credits.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        credits.Controls.Add(new Label { Text = "Authors", AutoSize = true, Margin = new Padding(0, 0, m * 3, rg) }, 0, 0);
        credits.Controls.Add(new Label { Text = "Contributions", AutoSize = true, Margin = new Padding(0, 0, 0, rg) }, 1, 0);
        credits.Controls.Add(new Label { Name = "AuthorsLabel", Text = "MultiFix: magoowa  |  HD-Widescreen: OBLPlife  |  Netplay: Oldkid.net", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, m * 3, 0) }, 0, 1);
        credits.Controls.Add(new Label { Name = "ContributionsLabel", Text = "Anakin94, peemosa, neparij", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0) }, 1, 1);
        footer.Controls.Add(credits, 1, 0);
        root.Controls.Add(footer);

        var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Dock = DockStyle.Fill };
        Button[] all = { play, install, restore, new Button { Name = "CloseButton", Text = "Close", DialogResult = DialogResult.Cancel } };
        play.Name = "PlayButton"; play.Text = "Play";
        install.Name = "InstallButton"; install.Text = "Install / Update";
        restore.Name = "RestoreButton"; restore.Text = "Restore original";
        foreach (var b in all) { b.AutoSize = true; b.AutoSizeMode = AutoSizeMode.GrowAndShrink; b.Padding = new Padding(Px(10), Px(4), Px(10), Px(4)); b.Margin = new Padding(0, 0, m / 2, 0); buttons.Controls.Add(b); }
        play.Font = new Font(Font, FontStyle.Bold);
        CancelButton = all[3];
        // The window runs modelessly (Application.Run), where a button's DialogResult does not close the form.
        all[3].Click += delegate { Close(); };
        var scroll = new Panel { Name = "ScrollPanel", Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(root);
        // The button bar uses the same margin as the content and stays at the bottom, outside the scrolling area.
        var bar = new Panel { Name = "ButtonBar", Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(m, m / 2, m, m) };
        buttons.Dock = DockStyle.None; buttons.Anchor = AnchorStyles.Top | AnchorStyles.Left; buttons.Margin = new Padding(0); buttons.Location = new Point(bar.Padding.Left, bar.Padding.Top);   // a docked child would not count for the bar's AutoSize
        bar.Controls.Add(buttons);
        Controls.Add(bar);
        Controls.Add(scroll);
        scroll.BringToFront();      // the fill panel is laid out last
        // The mouse wheel scrolls the content.
        scroll.MouseEnter += delegate { if (ActiveForm == this && !(ActiveControl is TextBox) && !(ActiveControl is ComboBox)) scroll.Focus(); };
        root.SizeChanged += delegate { scroll.AutoScrollMinSize = new Size(root.PreferredSize.Width, root.Height); };

        var found = Installer.Detect();
        if (initialFolder != null)
        {
            bool known = false;
            foreach (var g in found) if (string.Equals(g.Path, initialFolder, StringComparison.OrdinalIgnoreCase)) known = true;
            if (!known) found.Insert(0, new GameInstall { Path = initialFolder, Source = "Chosen" });
        }
        foreach (var g in found) installs.Items.Add(g);
        if (installs.Items.Count == 0) installs.Items.Add("No installation found - use Browse...");
        installs.SelectedIndexChanged += delegate { var g = installs.SelectedItem as GameInstall; if (g != null) SetFolder(g.Path); };
        path.TextChanged += delegate { RefreshInfo(); };
        browse.Click += delegate
        {
            using (var dialog = new FolderBrowserDialog { Description = "Select the folder that contains RoadRash95.exe", SelectedPath = path.Text })
                if (dialog.ShowDialog(this) == DialogResult.OK) ChooseFolder(dialog.SelectedPath);
        };
        play.Click += delegate { Play(); };
        install.Click += delegate { Run(false); };
        restore.Click += delegate { Run(true); };
        // Changes made to the ini outside this window (or by the game folder owner) show up when the window is focused again.
        Activated += delegate { if (!string.IsNullOrEmpty(path.Text)) { LoadOptions(path.Text); RefreshInfo(); } };
        if (installs.Items.Count > 0) installs.SelectedIndex = 0;
        RefreshInfo();
        FitToScreen(root, bar, scroll);     // last: the info table and the language row are filled in by now
        StartUpdateCheck();
    }

    // Downloads netplay (OKPlay) in the background: oldkid.net first, the GitHub release as fallback.
    void StartNetplayDownload()
    {
        if (netplayBusy) return;
        netplayBusy = true; netplayError = null;
        Say("Downloading netplay (OKPlay) from oldkid.net ...");
        RefreshInfo();
        string folder = path.Text;
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            string error, source = Installer.DownloadNetplay(folder, out error);
            MethodInvoker done = delegate
            {
                netplayBusy = false; netplayError = source == null ? error : null;
                Say(source != null ? "Netplay installed from " + source + ": wsock32.dll, netcheck.exe, okplay.ini and oldkid_netplay_readme.txt are in the game folder."
                                   : "Could not download netplay: " + error);
                RefreshInfo();
            };
            try { if (!IsDisposed) BeginInvoke(done); } catch (Exception) { }
        });
    }

    // Opens a web page in the default browser.
    void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { }
    }

    // A folder picked with Browse...: show it in the installations list.
    void ChooseFolder(string folder)
    {
        for (int i = 0; i < installs.Items.Count; ++i)
        {
            var g = installs.Items[i] as GameInstall;
            if (g != null && string.Equals(g.Path, folder, StringComparison.OrdinalIgnoreCase)) { installs.SelectedIndex = i; return; }
        }
        installs.Items.Insert(0, new GameInstall { Path = folder, Source = "Chosen" });
        installs.SelectedIndex = 0;
    }

    // Asks GitHub for the latest release in the background; a newer one is shown as a link under the MultiFix line.
    void StartUpdateCheck()
    {
        if (Environment.GetEnvironmentVariable("RRMF_NO_UPDATE_CHECK") == "1") return;
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            string version, url;
            if (!Installer.ParseLatestRelease(Installer.FetchLatestReleaseJson(), out version, out url)) return;
            if (!Installer.IsNewer(version, ModInfo.Version)) return;
            try
            {
                if (IsDisposed) return;
                MethodInvoker apply = delegate { latestVersion = version; latestUrl = url; RefreshInfo(); };
                if (IsHandleCreated) BeginInvoke(apply); else HandleCreated += delegate { BeginInvoke(apply); };
            }
            catch (Exception) { }
        });
    }

    // Tests: pretend the working area is this rectangle.
    internal static Rectangle? WorkingAreaOverride;
    // As large as the content needs, but never larger than the screen's working area; scroll bars take over beyond that.
    void FitToScreen(Control root, Control bar, ScrollableControl scroll)
    {
        Rectangle area = WorkingAreaOverride ?? Screen.FromPoint(Cursor.Position).WorkingArea;
        Size chrome = IsHandleCreated ? Size - ClientSize : SizeFromClientSize(Size.Empty);   // the real frame once the window exists
        root.PerformLayout();
        Size wanted = root.GetPreferredSize(Size.Empty);
        int barHeight = bar.GetPreferredSize(Size.Empty).Height;
        int maxWidth = Math.Max(Px(150), area.Width - chrome.Width), maxHeight = Math.Max(Px(100), area.Height - chrome.Height);
        int width = wanted.Width, height = wanted.Height + barHeight;
        bool vertical = height > maxHeight, horizontal = width + (vertical ? SystemInformation.VerticalScrollBarWidth : 0) > maxWidth;
        if (vertical || horizontal)
        {
            if (vertical) width += SystemInformation.VerticalScrollBarWidth;
            if (horizontal) height += SystemInformation.HorizontalScrollBarHeight;
        }
        scroll.AutoScrollMinSize = new Size(wanted.Width, wanted.Height);
        ClientSize = new Size(Math.Min(width, maxWidth), Math.Min(height, maxHeight));
        Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2));
    }

    void Say(string text) { status.Text = text; status.Visible = true; }

    void SetFolder(string folder)
    {
        path.Text = folder;
        LoadOptions(folder);
    }
    void LoadOptions(string folder)
    {
        loading = true;
        try
        {
            foreach (var box in boxes) { bool v = Installer.ReadOption(folder, (PatchOption)box.Tag); if (box.Checked != v) box.Checked = v; }
            int volume = Installer.ReadVideoVolume(folder);
            if (videoVolume.Value != volume) videoVolume.Value = volume;
            videoVolumeText.Text = volume + " %";
            var available = Installer.AvailableLanguages(folder);
            string current = Installer.ReadLanguage(folder);
            int index = available.FindIndex(n => string.Equals(n, current, StringComparison.OrdinalIgnoreCase));
            if (index < 0) index = 0;
            var old = new List<Control>();
            foreach (Control c in languageRow.Controls) old.Add(c);
            languageRow.Controls.Clear();
            foreach (var c in old) c.Dispose();
            for (int i = 0; i < available.Count; ++i)
            {
                var radio = new RadioButton { Text = available[i], Tag = available[i], AutoSize = true, Checked = i == index, Margin = new Padding(Px(3), 0, Px(14), 0) };
                radio.CheckedChanged += LanguageChanged;
                languageRow.Controls.Add(radio);
            }
        }
        finally { loading = false; }
        UpdateDependencies();
    }
    // Texture filtering exists only in the Direct3D renderer.
    void UpdateDependencies()
    {
        CheckBox d3d = null, filter = null;
        foreach (var box in boxes)
        {
            var o = (PatchOption)box.Tag;
            if (o.Key == "D3D11") d3d = box;
            if (o.Key == "TextureFilter") filter = box;
        }
        if (d3d != null && filter != null) filter.Enabled = d3d.Checked;
    }
    void VideoVolumeChanged(object sender, EventArgs e)
    {
        videoVolumeText.Text = videoVolume.Value + " %";
        if (loading) return;
        if (Installer.SaveVideoVolume(path.Text, videoVolume.Value))
            Say("Saved: Game/VideoVolume = " + videoVolume.Value + " (takes effect the next time a video plays)");
        else
            Say("RoadRashHD.ini does not exist yet; press Install / Update first to set the video volume.");
    }
    void LanguageChanged(object sender, EventArgs e)
    {
        var radio = sender as RadioButton;
        if (loading || radio == null || !radio.Checked) return;
        string name = (string)radio.Tag;
        if (Installer.SaveLanguage(path.Text, name))
            Say("Saved: Game/Language = " + name + " (takes effect the next time the game starts)");
        else
            Say("RoadRashHD.ini does not exist yet; press Install / Update first to choose a language.");
    }
    void OptionChanged(object sender, EventArgs e)
    {
        UpdateDependencies();
        if (loading) return;
        var box = (CheckBox)sender; var option = (PatchOption)box.Tag;
        if (Installer.SaveOption(path.Text, option, box.Checked))
            Say("Saved: " + option.Section + "/" + option.Key + " = " + ((box.Checked != option.Inverted) ? "1" : "0") + " (takes effect the next time the game starts)");
        else
            Say("RoadRashHD.ini does not exist yet; this choice is applied when you press Install / Update.");
    }
    void RefreshInfo()
    {
        int m = Margin12, rg = RowGap, wrap = Px(520);
        infoTable.SuspendLayout();
        infoTable.Controls.Clear(); infoTable.RowStyles.Clear(); infoTable.RowCount = 0;
        Func<string, Label> key = text => new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, rg, m, rg) };
        int row = 0;
        Action<string, Control> addRow = (name, value) =>
        {
            infoTable.RowCount = row + 1; infoTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            infoTable.Controls.Add(key(name), 0, row);
            value.Anchor = AnchorStyles.Left; value.Margin = new Padding(0, rg, 0, rg);
            infoTable.Controls.Add(value, 1, row);
            ++row;
        };
        foreach (string line in Installer.Describe(path.Text).Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = line.IndexOf(": ", StringComparison.Ordinal);
            if (colon > 0 && colon < 16)
            {
                string name = line.Substring(0, colon + 1);
                if (name == "Edition:") continue;       // the edition is part of the installations list
                addRow(name, new Label { Text = line.Substring(colon + 2), AutoSize = true, MaximumSize = new Size(wrap, 0) });
                if (name == "MultiFix:" && latestVersion != null)
                {
                    var update = new LinkLabel { Name = "UpdateLink", Text = "Version " + latestVersion + " is available on GitHub - click to open the release", AutoSize = true, MaximumSize = new Size(wrap, 0) };
                    update.LinkClicked += delegate { OpenUrl(latestUrl ?? Installer.LatestReleaseUrl); };
                    tips.SetToolTip(update, latestUrl ?? Installer.LatestReleaseUrl);
                    addRow("", update);
                }
            }
            else
            {
                infoTable.RowCount = row + 1; infoTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var single = new Label { Text = line, AutoSize = true, MaximumSize = new Size(wrap, 0), Margin = new Padding(0, rg, 0, rg) };
                infoTable.Controls.Add(single, 0, row); infoTable.SetColumnSpan(single, 2); ++row;
            }
        }
        // Language, directly under the MultiFix line.
        addRow("Language:", languageRow);
        // Netplay (OKPlay) needs a wsock32.dll next to the game.
        string datecode = Installer.NetplayDatecode(path.Text);
        if (datecode != null) addRow("Netplay:", new Label { Name = "NetplayLabel", Text = "wsock32.dll present (datecode: " + datecode + ")", AutoSize = true });
        else
        {
            var missing = new FlowLayoutPanel { Name = "NetplayPanel", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            missing.Controls.Add(new Label { Text = "wsock32.dll not found -", AutoSize = true, Margin = new Padding(0, 0, rg * 2, 0) });
            var link = new LinkLabel { Name = "NetplayLink", Text = netplayBusy ? "downloading..." : "download netplay", AutoSize = true, Margin = new Padding(0), Enabled = !netplayBusy };
            link.LinkClicked += delegate { StartNetplayDownload(); };
            tips.SetToolTip(link, "Downloads OKPlay (oldkid.net; GitHub as fallback) and unpacks it into the game folder");
            missing.Controls.Add(link);
            if (netplayError != null)   // the download failed: point to the project page
            {
                var page = new LinkLabel { Name = "NetplayPage", Text = "OKPlay on GitHub", AutoSize = true, Margin = new Padding(rg * 3, 0, 0, 0) };
                page.LinkClicked += delegate { OpenUrl(Installer.OkPlayUrl); };
                tips.SetToolTip(page, Installer.OkPlayUrl);
                missing.Controls.Add(page);
            }
            addRow("Netplay:", missing);
        }
        infoTable.ResumeLayout();
        bool game = System.IO.File.Exists(System.IO.Path.Combine(path.Text ?? "", "RoadRash95.exe"));
        play.Enabled = install.Enabled = restore.Enabled = game;
    }
    void Play()
    {
        var status = Installer.Status(path.Text);
        if (!status.Installed || status.Outdated)
        {
            // Yes installs/updates first, No starts the game as it is; either way the game starts.
            string question = !status.Installed
                ? "MultiFix was not detected in this game folder.\n\nInstall it before starting the game?"
                : "The MultiFix DLL in the game folder is older or different (" + (status.RendererVersion != "" ? status.RendererVersion : "unknown version") + ", this launcher has " + ModInfo.Version + ").\n\nUpdate it before starting the game?";
            if (MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                var options = new Dictionary<string, bool>();
                foreach (var box in boxes) { var o = (PatchOption)box.Tag; options[o.Section + "/" + o.Key] = box.Checked; }
                var result = Installer.Execute(false, path.Text, options);
                RefreshInfo();
                if (!result.Success) MessageBox.Show(this, result.Message + "\n\nThe game will start without the update.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        string error = Installer.Launch(path.Text);
        if (error != null) { MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        Close();
    }
    void Run(bool restoring)
    {
        PatchResult result;
        play.Enabled = install.Enabled = restore.Enabled = false;
        UseWaitCursor = true;
        try
        {
            var options = new Dictionary<string, bool>();
            foreach (var box in boxes) { var o = (PatchOption)box.Tag; options[o.Section + "/" + o.Key] = box.Checked; }
            result = Installer.Execute(restoring, path.Text, options);
        }
        finally { UseWaitCursor = false; }
        RefreshInfo();
        LoadOptions(path.Text);
        MessageBox.Show(this, result.Message, Text, MessageBoxButtons.OK,
                        result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
    }
}
