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
    sealed class SliderRow { public PatchSlider Slider; public TrackBar Bar; public Label Value, Name, Hint; public FlowLayoutPanel Line; }
    readonly List<SliderRow> sliderRows = new List<SliderRow>();
    readonly List<TableLayoutPanel> pageTables = new List<TableLayoutPanel>();
    TabControl tabs; Control musicHelpRow; TableLayoutPanel musicHelpTable;
    TableLayoutPanel laaTable; CheckBox laaBox; Label laaHint;      // the 4 GB (Large Address Aware) switch above the tabs
    string latestNetplay;                                            // newest okplay version known from GitHub / oldkid.net
    readonly FlowLayoutPanel languageRow = new FlowLayoutPanel();   // one radio button per language
    bool loading;
    int infoWrap;                                                    // width limit of the info lines in the compact layout
    bool compact;                                                    // narrow screens (low resolution / big display scale): one column, text wraps
    readonly List<Label> optionHints = new List<Label>();
    PictureBox logoBox; TableLayoutPanel headTable, footerTable, rootPanel; Control creditsTable; FlowLayoutPanel buttonRow;
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
        var root = rootPanel = new TableLayoutPanel
        {
            Name = "RootPanel", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Top,
            Padding = new Padding(m), MinimumSize = new Size(Px(700), 0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        // Top block: the logo on the left (a click opens the project page), installations and info on the right.
        var head = headTable = new TableLayoutPanel { Name = "HeadTable", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, m) };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F)); head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));   // first column: as wide as the option names, so the info block starts on the description column
        try
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png"))
                if (stream != null)
                {
                    var copy = new System.IO.MemoryStream();
                    stream.CopyTo(copy); copy.Position = 0;
                    var image = Image.FromStream(copy);
                    int width = Px(170), height = width * image.Height / image.Width;
                    var logo = logoBox = new PictureBox { Name = "LogoBox", Image = image, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(width, height), Margin = new Padding(0, 0, m, 0), Anchor = AnchorStyles.Top | AnchorStyles.Left, Cursor = Cursors.Hand };
                    tips.SetToolTip(logo, Installer.ProjectUrl);
                    logo.Click += delegate { OpenUrl(Installer.ProjectUrl); };
                    head.Controls.Add(logo, 0, 0);
                    head.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, width + m);
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

        // The 4 GB (Large Address Aware) switch: the one thing in this window that changes RoadRash95.exe, so it sits on its own above the tabs.
        laaTable = new TableLayoutPanel { Name = "LaaBlock", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, m) };
        laaTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); laaTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        laaBox = new CheckBox { Name = "LaaBox", Text = "4 GB Memory (Large Address Aware)", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, rg, m, rg) };
        laaHint = new Label { Name = "LaaHint", Text = "", AutoSize = true, ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Left, Margin = new Padding(0, rg, 0, rg) };
        tips.SetToolTip(laaBox, "Lets the 32-bit game use up to 4 GB of memory on 64-bit Windows instead of 2 GB (helps against missing textures and crashes after long races). " +
                                 "Changes one flag in RoadRash95.exe; the original is kept as RoadRash95.exe.multifix-backup. Unchecking it or Restore original puts it back.");
        laaBox.CheckedChanged += LaaChanged;
        laaTable.Controls.Add(laaBox, 0, 0); laaTable.Controls.Add(laaHint, 1, 0);
        root.Controls.Add(laaTable);

        // The options, grouped in tabs (Stability first). Every option has its own row and is read from / written to RoadRashHD.ini as it changes.
        tabs = new TabControl { Name = "OptionsTabs", Dock = DockStyle.Fill, Multiline = true, Margin = new Padding(0, 0, 0, m), Padding = new Point(Px(10), Px(3)) };
        foreach (string category in Installer.Categories)
        {
            var page = new TabPage(category) { Name = "Tab_" + category.Replace(' ', '_'), UseVisualStyleBackColor = true, Padding = new Padding(m / 2) };
            var rows = new TableLayoutPanel { Name = "OptionsTable_" + category.Replace(' ', '_'), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Dock = DockStyle.Top, Margin = new Padding(0) };
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            int r = 0;
            foreach (var option in Installer.Options)
            {
                if (option.Category != category) continue;
                var box = new CheckBox { Text = option.Label, Tag = option, AutoSize = true, Checked = option.Default, Anchor = AnchorStyles.Left, Margin = new Padding(0, rg, m, rg) };
                box.Name = "Option_" + option.Section + "_" + option.Key;
                var hint = new Label { Text = option.Hint, AutoSize = true, ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Left, Margin = new Padding(0, rg, 0, rg) };
                tips.SetToolTip(box, option.Section + "/" + option.Key + " - " + option.Hint);
                box.CheckedChanged += OptionChanged;
                boxes.Add(box); optionHints.Add(hint);
                rows.Controls.Add(box, 0, r); rows.Controls.Add(hint, 1, r); ++r;
            }
            // Sliders: the name under the option names (lined up with their text), the slider and its value in the column of the descriptions,
            // the description on the next line.
            foreach (var slider in Installer.Sliders)
            {
                if (slider.Category != category) continue;
                var row = new SliderRow { Slider = slider };
                row.Name = new Label { Text = slider.Label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(Px(13), rg, m, rg) };
                row.Bar = new TrackBar { Name = "Slider_" + slider.Section + "_" + slider.Key, Minimum = slider.Min, Maximum = slider.Max, TickFrequency = 10, SmallChange = 5, LargeChange = 10, Value = slider.Default,
                                         AutoSize = false, Size = new Size(Px(190), Px(42)), Margin = new Padding(0, 0, m, 0), Tag = row };
                row.Value = new Label { Name = "SliderText_" + slider.Section + "_" + slider.Key, Text = slider.Default + " %", AutoSize = true, Anchor = AnchorStyles.Left, MinimumSize = new Size(Px(44), 0) };
                row.Line = new FlowLayoutPanel { Name = "SliderLine_" + slider.Key, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
                row.Line.Controls.Add(row.Bar); row.Line.Controls.Add(row.Value);
                row.Hint = new Label { Text = slider.Hint, AutoSize = true, ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Left, Margin = new Padding(0, rg, 0, rg) };
                tips.SetToolTip(row.Bar, slider.Section + "/" + slider.Key + " - " + slider.Hint);
                row.Bar.ValueChanged += SliderChanged;
                sliderRows.Add(row);
                rows.Controls.Add(row.Name, 0, r); rows.Controls.Add(row.Line, 1, r); ++r;
                rows.Controls.Add(row.Hint, 1, r); ++r;
            }
            if (category == "Audio")
            {
                // Custom music: a click explains it, another opens the playlist folder.
                var links = new FlowLayoutPanel { Name = "MusicHelpRow", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, rg * 4, 0, 0) };
                var help = new Button { Name = "MusicHelpButton", Text = "Custom music (WAV): how it works", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(Px(10), Px(4), Px(10), Px(4)), Margin = new Padding(0, 0, m / 2, 0) };
                var folder = new Button { Name = "MusicFolderButton", Text = "Open the Playlist folder", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(Px(10), Px(4), Px(10), Px(4)), Margin = new Padding(0) };
                tips.SetToolTip(help, "Explains WAV Music Replacement and Music Playlist: where to put the files, which formats work, how a track gets its file");
                tips.SetToolTip(folder, "Opens AUDIO\\MUSIC\\Playlist in the game folder (it is created when it does not exist)");
                help.Click += delegate { ShowMusicHelp(); };
                folder.Click += delegate { OpenMusicFolder(true); };
                links.Controls.Add(help); links.Controls.Add(folder);
                rows.Controls.Add(links, 0, r); rows.SetColumnSpan(links, 2); ++r;
                musicHelpRow = links; musicHelpTable = rows;
            }
            page.Controls.Add(rows); pageTables.Add(rows); tabs.TabPages.Add(page);
        }
        root.Controls.Add(tabs);

        status.Name = "StatusLabel"; status.Visible = false; status.AutoSize = true; status.MaximumSize = new Size(wrap, 0); status.ForeColor = SystemColors.GrayText; status.Margin = new Padding(0, 0, 0, m);
        root.Controls.Add(status);

        // The credits, two thin lines at the bottom.
        var footer = footerTable = new TableLayoutPanel { Name = "FooterTable", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 1, Margin = new Padding(0, 0, 0, m) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var credits = new TableLayoutPanel { Name = "AuthorsTable", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Anchor = AnchorStyles.Top | AnchorStyles.Left, Margin = new Padding(0) };
        credits.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        credits.Controls.Add(new Label { Name = "AuthorsLabel", Text = "Authors: MultiFix: magoowa  |  HD-Widescreen: OBLPlife  |  Netplay: Oldkid.net  |  DualSense: CypherCripto", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, rg) }, 0, 0);
        credits.Controls.Add(new Label { Name = "ContributionsLabel", Text = "Contributions: Anakin94, peemosa, neparij, jmb20, ChilliChickenDry", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0) }, 0, 1);
        footer.Controls.Add(credits, 0, 0); creditsTable = credits;
        root.Controls.Add(footer);

        var buttons = buttonRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Dock = DockStyle.Fill };
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
        // A horizontal scroll bar only when the window is really narrower than the content (not because a vertical bar took its width).
        root.SizeChanged += delegate
        {
            int need = root.PreferredSize.Width + (scroll.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0);
            scroll.AutoScrollMinSize = new Size(ClientSize.Width >= need ? 0 : root.PreferredSize.Width, root.Height);
        };

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
        // A screen that is too narrow for the two-column layout (low resolution, or a big display scale on a small screen): one column.
        {
            Rectangle area = WorkingAreaOverride ?? Screen.FromPoint(Cursor.Position).WorkingArea;
            Size chrome = SizeFromClientSize(Size.Empty);
            FitTabs();
            root.PerformLayout();
            if (root.GetPreferredSize(Size.Empty).Width + SystemInformation.VerticalScrollBarWidth > area.Width - chrome.Width)
                SetCompact(area.Width - chrome.Width - SystemInformation.VerticalScrollBarWidth);
            if (compact) RefreshInfo();        // the info lines were built with the wide-layout limits
        }
        FitTabs();
        FitToScreen(root, bar, scroll);     // last: the info table and the language row are filled in by now
        StartUpdateCheck();
        StartNetplayCheck();
    }

    // Asks GitHub (the okplay tags) for the newest netplay version in the background; a newer one than the installed is offered as an update.
    void StartNetplayCheck()
    {
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            string version = Installer.FetchLatestNetplayVersion();
            if (version == null) return;
            try
            {
                if (IsDisposed) return;
                MethodInvoker apply = delegate { latestNetplay = version; RefreshInfo(); };
                if (IsHandleCreated) BeginInvoke(apply); else HandleCreated += delegate { BeginInvoke(apply); };
            }
            catch (Exception) { }
        });
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
    // One column: every option is its name with the description under it, text wraps to the available width, the logo is small,
    // the head, the footer and the buttons stack. The scroll area then only has to scroll vertically.
    void SetCompact(int available)
    {
        compact = true;
        int m = Margin12, rg = RowGap, text = Math.Max(Px(120), available - 2 * m);
        infoWrap = Math.Max(Px(100), text - (Px(80) + m) - Px(110));
        SuspendLayout();
        rootPanel.MinimumSize = new Size(0, 0);
        if (logoBox != null)
        {
            int w = Px(80); logoBox.Size = new Size(w, logoBox.Image.Height * w / logoBox.Image.Width); logoBox.Margin = new Padding(0, 0, m, 0);
            headTable.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, w + m);
        }
        // the options: in every tab the name, the description under it
        int indent = Px(20);
        foreach (var table in pageTables)
        {
            table.SuspendLayout();
            var controls = new List<Control>(); foreach (Control c in table.Controls) controls.Add(c);
            table.Controls.Clear();
            table.RowStyles.Clear(); table.ColumnStyles.Clear(); table.ColumnCount = 1; table.RowCount = 0;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            int r = 0;
            foreach (var c in controls)
            {
                var box = c as CheckBox; var sliderRow = c.Tag as SliderRow;
                int i = box == null ? -1 : boxes.IndexOf(box);
                if (box != null && i >= 0)
                {
                    box.Margin = new Padding(0, rg * 2, 0, 0);
                    optionHints[i].Margin = new Padding(indent, 0, 0, rg); optionHints[i].MaximumSize = new Size(text - indent - Px(30), 0);
                    table.Controls.Add(box, 0, r++); table.Controls.Add(optionHints[i], 0, r++);
                }
            }
            foreach (var row in sliderRows)
            {
                if (row.Name.Parent == null && !controls.Contains(row.Name)) continue;
                row.Name.Margin = new Padding(Px(13), rg * 2, 0, 0); row.Hint.Margin = new Padding(indent, 0, 0, rg); row.Hint.MaximumSize = new Size(text - indent - Px(30), 0);
                row.Line.Margin = new Padding(indent, 0, 0, 0);
                table.Controls.Add(row.Name, 0, r++); table.Controls.Add(row.Line, 0, r++); table.Controls.Add(row.Hint, 0, r++);
            }
            if (table == musicHelpTable && musicHelpRow != null)
            {
                var flow = (FlowLayoutPanel)musicHelpRow; flow.WrapContents = true; flow.MaximumSize = new Size(text - Px(30), 0); flow.Margin = new Padding(0, rg * 3, 0, 0);
                table.Controls.Add(flow, 0, r++);
            }
            table.ResumeLayout(true);
        }
        laaHint.MaximumSize = new Size(text - Px(30), 0);
        laaTable.SuspendLayout(); laaTable.Controls.Clear(); laaTable.ColumnStyles.Clear(); laaTable.ColumnCount = 1;
        laaTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        laaBox.Margin = new Padding(0, rg * 2, 0, 0); laaHint.Margin = new Padding(indent, 0, 0, rg);
        laaTable.Controls.Add(laaBox, 0, 0); laaTable.Controls.Add(laaHint, 0, 1); laaTable.ResumeLayout(true);
        installs.MaximumSize = new Size(Math.Max(Px(100), infoWrap - Px(70)), 0);
        // info block: the language buttons wrap
        languageRow.WrapContents = true; languageRow.MaximumSize = new Size(infoWrap, 0);
        status.MaximumSize = new Size(text, 0);
        // footer: the credits wrap
        foreach (Control c in creditsTable.Controls) c.MaximumSize = new Size(text, 0);
        // the buttons wrap too
        buttonRow.WrapContents = true; buttonRow.MaximumSize = new Size(text, 0);
        ResumeLayout(true);
        FitTabs();
    }

    void FitToScreen(Control root, Control bar, ScrollableControl scroll, bool recenter = true)
    {
        fitRoot = root; fitBar = bar; fitScroll = scroll;
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
        scroll.AutoScrollMinSize = new Size(horizontal ? wanted.Width : 0, wanted.Height);
        if (!recenter) { width = Math.Max(width, ClientSize.Width); height = Math.Max(height, ClientSize.Height); }   // refitting only grows the window
        ClientSize = new Size(Math.Min(width, maxWidth), Math.Min(height, maxHeight));
        if (recenter) Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2));
        else Location = new Point(Location.X, Math.Max(area.Top, Math.Min(Location.Y, area.Bottom - Height)));
    }

    // The status line appears below the options: grow the window by its height (once) so nothing needs a scroll bar.
    void Say(string text)
    {
        status.Text = text; status.Visible = true;
        Refit();
    }
    Control fitRoot, fitBar; ScrollableControl fitScroll;
    // The content changed (status line, info rows after a download or an install): let the window grow so no scroll bar appears.
    void Refit() { if (IsHandleCreated && fitRoot != null) FitToScreen(fitRoot, fitBar, fitScroll, false); }

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
            foreach (var row in sliderRows)
            {
                int value = Installer.ReadSlider(folder, row.Slider);
                if (row.Bar.Value != value) row.Bar.Value = value;
                row.Value.Text = value + " %";
            }
            RefreshLaa();
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
    void SliderChanged(object sender, EventArgs e)
    {
        var bar = (TrackBar)sender; var row = (SliderRow)bar.Tag;
        row.Value.Text = bar.Value + " %";
        if (loading) return;
        if (Installer.SaveSlider(path.Text, row.Slider, bar.Value))
            Say("Saved: " + row.Slider.Section + "/" + row.Slider.Key + " = " + bar.Value + (row.Slider.Key == "VideoVolume" ? " (takes effect the next time a video plays)" : " (takes effect the next time the game starts)"));
        else
            Say("RoadRashHD.ini does not exist yet; press Install / Update first to set " + row.Slider.Label + ".");
    }
    // The 4 GB switch: shows the state of RoadRash95.exe and changes it (with a backup) when it is toggled.
    void RefreshLaa()
    {
        if (laaBox == null) return;
        var state = Installer.Laa(path.Text);
        bool wasLoading = loading; loading = true;
        try
        {
            laaBox.Enabled = state.ExeFound && state.Valid && state.Is64BitWindows;
            laaBox.Checked = state.Patched;
            if (!state.ExeFound) laaHint.Text = "RoadRash95.exe was not found";
            else if (!state.Valid) laaHint.Text = "RoadRash95.exe is not a 32-bit Windows program this switch understands";
            else if (!state.Is64BitWindows) laaHint.Text = "Needs 64-bit Windows (this one is 32-bit)";
            else if (state.Patched) laaHint.Text = state.BackupExists ? "Patched - the original is kept as RoadRash95.exe.multifix-backup" : "Patched (no backup of the original found)";
            else laaHint.Text = "Not patched - the game gets 2 GB of memory. Changes RoadRash95.exe; a backup is made";
        }
        finally { loading = wasLoading; }
    }
    void LaaChanged(object sender, EventArgs e)
    {
        if (loading) return;
        bool enable = laaBox.Checked;
        if (enable)
        {
            var answer = MessageBox.Show(this, "This changes one flag in RoadRash95.exe so that the game can use up to 4 GB of memory on 64-bit Windows (it helps against missing textures and crashes after long races).\n\n" +
                "The original file is kept as RoadRash95.exe.multifix-backup. Unchecking this box or Restore original puts it back. Steam, GOG Galaxy or the EA app may restore the file when they verify the game; then switch it on again.\n\nContinue?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) { RefreshLaa(); return; }
        }
        UseWaitCursor = true;
        string error;
        try { error = Installer.SetLarge(path.Text, enable); }
        finally { UseWaitCursor = false; }
        RefreshLaa();
        if (error != null) { MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        RefreshInfo();
        Say(enable ? "RoadRash95.exe now uses up to 4 GB of memory (the original is kept as RoadRash95.exe.multifix-backup)." : "RoadRash95.exe is back to its original state.");
    }
    // ---- custom music help ----
    const string MusicHelpText =
        "CUSTOM MUSIC (WAV)\r\n" +
        "The game's music is 14 files in AUDIO\\MUSIC, named like T_TEETH.RRA. An .RRA is a WAV file with another extension (8-bit, 22.05 kHz, stereo). " +
        "With WAV Music Replacement (off by default: switch it on in the Audio tab, together with Music Playlist for the playlist) the game can play your own WAV files instead.\r\n\r\n" +
        "1. REPLACE ONE TRACK\r\n" +
        "Put a WAV with the same name as the track in AUDIO\\MUSIC, for example AUDIO\\MUSIC\\T_TEETH.WAV next to T_TEETH.RRA. The original .RRA stays where it is; remove the .WAV to go back.\r\n" +
        "The 14 tracks: HB_PASS, HB_TRIP, MM_DINO, P_BRIDGE, P_JESSIE, P_PANSY, SD_DUEL, SD_TRAIN, SG_KICKS, SG_OUTSH, SG_RUSTY, SG_SUPER, T_AUTO, T_TEETH.\r\n\r\n" +
        "2. ANY FILE NAME\r\n" +
        "Add the track and the file you want to the [MusicFiles] section of RoadRashHD.ini, one line per track:\r\n" +
        "    [MusicFiles]\r\n" +
        "    T_TEETH=My remaster of Teeth.wav\r\n" +
        "    SG_KICKS=D:\\Music\\kicks.wav\r\n" +
        "The file is a name in AUDIO\\MUSIC, a path relative to the game folder (for example Music\\kicks.wav or AUDIO\\MY MUSIC\\kicks.wav; slashes work too) or a full path (any folder or drive).\r\n\r\n" +
        "3. PLAYLIST (Music Playlist)\r\n" +
        "Every WAV in AUDIO\\MUSIC\\Playlist is music for the game. Each time the game starts a piece of music (menu, race, restart) it gets the next file: random order, every file once before any repeats, never the same piece twice in a row. " +
        "In RoadRashHD.ini, [Audio]: MusicPlaylistShuffle=0 plays them by file name, MusicPlaylistFolder= picks another folder (relative to the game folder, or a full path). The folder is read again each time the list runs out, so new files are found without restarting the game.\r\n\r\n" +
        "WHICH FILE A TRACK GETS\r\n" +
        "1) its line in [MusicFiles], 2) AUDIO\\MUSIC\\TRACK.WAV, 3) the next file of the playlist, 4) the original .RRA. A track with 1) or 2) keeps it; the playlist fills the rest.\r\n\r\n" +
        "FORMATS\r\n" +
        "PCM WAV, 8-bit or 16-bit, mono or stereo, any sample rate. 44.1 kHz (CD quality) and 48 kHz files are both fine, and so are 22.05 and 32 kHz or 96 kHz. Not supported (the file is skipped and the original plays): 32-bit float, 24-bit, WAVE_FORMAT_EXTENSIBLE, MP3/FLAC/OGG. Convert such files to 16-bit PCM WAV first (for example with Audacity).\r\n" +
        "Files are read from disk while they play, so a 16-bit 44.1 kHz stereo file needs about 170 KB per second. The game's music slider (Sound Mix) still controls the volume.\r\n\r\n" +
        "IF SOMETHING DOES NOT PLAY\r\n" +
        "RoadRashHD.log in the game folder says what happened: \"music: T_TEETH plays from ...\" (it works), \"music: [MusicFiles] ...: file not found\" (wrong name or path), \"music: playlist skips ...\" (a file with an unsupported format), \"music: playlist of N WAV files\" (the playlist was found). " +
        "Both options are off until you switch them on in the Audio tab (WAV Music Replacement off = the game plays only its own .RRA files).";
    void ShowMusicHelp()
    {
        using (var dialog = new Form())
        {
            dialog.Text = "Custom music (WAV)"; dialog.Font = Font; dialog.StartPosition = FormStartPosition.CenterParent; dialog.ShowIcon = false; dialog.MinimizeBox = false; dialog.MaximizeBox = true;
            dialog.FormBorderStyle = FormBorderStyle.Sizable; dialog.ClientSize = new Size(Px(660), Px(520)); dialog.MinimumSize = new Size(Px(380), Px(260));
            var box = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Text = MusicHelpText, BackColor = SystemColors.Window, BorderStyle = BorderStyle.None, Margin = new Padding(0) };
            var holder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Margin12) };
            holder.Controls.Add(box);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(Margin12, Margin12 / 2, Margin12, Margin12) };
            var openMusic = new Button { Text = "Open the Music folder", AutoSize = true, Padding = new Padding(Px(10), Px(4), Px(10), Px(4)) };
            var openPlaylist = new Button { Text = "Open the Playlist folder", AutoSize = true, Padding = new Padding(Px(10), Px(4), Px(10), Px(4)) };
            var close = new Button { Text = "Close", AutoSize = true, Padding = new Padding(Px(10), Px(4), Px(10), Px(4)), DialogResult = DialogResult.OK };
            openMusic.Click += delegate { OpenMusicFolder(false); };
            openPlaylist.Click += delegate { OpenMusicFolder(true); };
            bar.Controls.Add(openMusic); bar.Controls.Add(openPlaylist); bar.Controls.Add(close);
            dialog.Controls.Add(holder); dialog.Controls.Add(bar);
            dialog.AcceptButton = close; dialog.CancelButton = close;
            holder.BringToFront();
            dialog.ShowDialog(this);
        }
    }
    // The folder named by MusicPlaylistFolder in RoadRashHD.ini ([Audio]); a full path, or relative to the game folder. Empty when not set.
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern uint GetPrivateProfileString(string section, string key, string def, System.Text.StringBuilder result, uint size, string file);
    static string ConfiguredPlaylistFolder(string game)
    {
        try
        {
            string ini = System.IO.Path.Combine(game, "RoadRashHD.ini");
            if (!System.IO.File.Exists(ini)) return "";
            var buffer = new System.Text.StringBuilder(1024);
            GetPrivateProfileString("Audio", "MusicPlaylistFolder", "", buffer, (uint)buffer.Capacity, ini);   // the API the game uses: section, exact key, quotes
            string value = buffer.ToString().Trim();
            if (value.Length == 0) return "";
            bool absolute = value.Length > 1 && (value[1] == ':' || (value[0] == '\\' && value[1] == '\\'));    // the same rule the game uses
            return absolute ? value : System.IO.Path.Combine(game, value);
        }
        catch (Exception) { }
        return "";
    }
    // Opens AUDIO\MUSIC (or AUDIO\MUSIC\Playlist, created when missing) of the chosen game in Explorer.
    void OpenMusicFolder(bool playlist)
    {
        try
        {
            string game = path.Text;
            if (string.IsNullOrEmpty(game) || !System.IO.Directory.Exists(game)) { Say("Choose the game folder first."); return; }
            string folder = System.IO.Path.Combine(game, "AUDIO", "MUSIC");
            if (playlist)
            {
                folder = System.IO.Path.Combine(folder, "Playlist");
                string custom = ConfiguredPlaylistFolder(game);       // [Audio] MusicPlaylistFolder: the game reads the same folder
                if (!string.IsNullOrEmpty(custom)) folder = custom;
            }
            if (!System.IO.Directory.Exists(folder))
            {
                if (!playlist) { Say("The folder AUDIO\\MUSIC was not found in this game folder."); return; }
                System.IO.Directory.CreateDirectory(folder);
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
        }
        catch (Exception error) { Say("Could not open the folder: " + error.Message); }
    }
    // Fits the tab control to its tallest page and widest table (a TabControl does not size itself to its content).
    void FitTabs()
    {
        if (tabs == null || pageTables.Count == 0) return;
        int m = Margin12, tallest = 0, widest = 0;
        foreach (var table in pageTables)
        {
            table.PerformLayout();
            Size size = table.GetPreferredSize(Size.Empty);
            tallest = Math.Max(tallest, size.Height); widest = Math.Max(widest, size.Width);
        }
        int rows = tabs.IsHandleCreated ? Math.Max(1, tabs.RowCount) : 1;
        int header = (tabs.ItemSize.Height > 0 ? tabs.ItemSize.Height : Font.Height + Px(10)) * rows + Px(8);
        int height = tallest + header + m + Px(4);
        int width = widest + m + (compact ? Px(12) : Px(48));
        tabs.MinimumSize = Size.Empty; tabs.Size = new Size(width, height); tabs.MinimumSize = new Size(width, height);
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
        int m = Margin12, rg = RowGap, wrap = compact ? infoWrap : Px(520);
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
        if (datecode != null)
        {
            string installed = Installer.NetplayVersion(path.Text);
            var present = new FlowLayoutPanel { Name = "NetplayPanel", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = compact, MaximumSize = compact ? new Size(wrap, 0) : Size.Empty };
            present.Controls.Add(new Label { Name = "NetplayLabel", Text = "wsock32.dll present (" + (installed != null ? "v" + installed + ", " : "") + "datecode: " + datecode + ")", AutoSize = true, Margin = new Padding(0, 0, rg * 2, 0) });
            if (latestNetplay != null && (installed == null || Installer.IsNewer(latestNetplay, installed)))
            {
                var update = new LinkLabel { Name = "NetplayUpdate", Text = netplayBusy ? "downloading..." : "v" + latestNetplay + " is available - update netplay", AutoSize = true, Margin = new Padding(0), Enabled = !netplayBusy };
                update.LinkClicked += delegate { StartNetplayDownload(); };
                tips.SetToolTip(update, "Downloads the newest OKPlay (the version is checked on GitHub, the zip comes from oldkid.net) and unpacks it into the game folder; your okplay.ini is kept");
                present.Controls.Add(update);
            }
            addRow("Netplay:", present);
        }
        else
        {
            var missing = new FlowLayoutPanel { Name = "NetplayPanel", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = compact, MaximumSize = compact ? new Size(wrap, 0) : Size.Empty };
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
        Refit();
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
            if (restoring && Installer.Laa(path.Text).Patched)
            {
                string laaError = Installer.SetLarge(path.Text, false);
                result = new PatchResult(result.Success && laaError == null, result.Message + (laaError == null ? "\nRoadRash95.exe was restored to its original state (4 GB switch off)." : "\nRoadRash95.exe could not be restored: " + laaError));
            }
        }
        finally { UseWaitCursor = false; }
        RefreshInfo();
        LoadOptions(path.Text);
        MessageBox.Show(this, result.Message, Text, MessageBoxButtons.OK,
                        result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
    }
}
