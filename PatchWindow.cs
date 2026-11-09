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

// Installer + launcher window. The layout is built from tables that size themselves from the font,
// so it stays correct at any display scale (100 % - 300 %). Every option has its own row and is read
// from / written to RoadRashHD.ini as it changes.
internal sealed class PatchWindow : Form
{
    readonly ComboBox installs = new ComboBox();
    readonly TextBox path = new TextBox();
    readonly Button browse = new Button(), play = new Button(), install = new Button(), restore = new Button();
    readonly TableLayoutPanel infoTable = new TableLayoutPanel();
    readonly Label status = new Label();
    readonly List<CheckBox> boxes = new List<CheckBox>();
    readonly ToolTip tips = new ToolTip();
    readonly TrackBar videoVolume = new TrackBar();                  // [Game] VideoVolume
    readonly Label videoVolumeText = new Label();
    readonly FlowLayoutPanel languageRow = new FlowLayoutPanel();   // one radio button per language
    bool loading;

    // All sizes derive from the font height, which already follows the display scale (100 % ... 300 %).
    int Px(int value) { return (int)Math.Round(value * Font.Height / 15.0); }

    public PatchWindow() : this(null) { }

    public PatchWindow(string initialFolder)
    {
        Text = Installer.Title;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        try
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                if (stream != null) Icon = new Icon(stream);
        }
        catch (Exception) { }

        int gap = Px(6), wrap = Px(600);
        var root = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill,
            Padding = new Padding(Px(14)), MinimumSize = new Size(Px(700), 0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        // Logo.
        var header = new Panel { AutoSize = false, Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 0, gap * 2) };
        try
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png"))
                if (stream != null)
                {
                    var copy = new System.IO.MemoryStream();
                    stream.CopyTo(copy); copy.Position = 0;
                    var image = Image.FromStream(copy);
                    int width = Px(300), height = width * image.Height / image.Width;
                    var logo = new PictureBox { Name = "LogoBox", Image = image, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(width, height), Location = new Point(0, 0) };
                    header.Controls.Add(logo);
                    header.Size = new Size(width, height);
                }
        }
        catch (Exception) { }
        root.Controls.Add(header);

        // Installations and game folder.
        var top = new TableLayoutPanel { Name = "TopTable", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, gap) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(new Label { Text = "Detected installations", AutoSize = true, Margin = new Padding(0, 0, 0, 2) }, 0, 0);
        installs.Name = "InstallsList"; installs.DropDownStyle = ComboBoxStyle.DropDownList; installs.Dock = DockStyle.Fill; installs.Margin = new Padding(Px(3), 0, gap, gap);
        browse.Name = "BrowseButton"; browse.Text = "Browse..."; browse.AutoSize = true; browse.Margin = new Padding(0, 0, gap * 2, gap);
        top.Controls.Add(installs, 0, 1); top.Controls.Add(browse, 1, 1);
        top.Controls.Add(new Label { Text = "Game folder", AutoSize = true, Margin = new Padding(0, 0, 0, 2) }, 0, 2);
        path.Name = "FolderBox"; path.Dock = DockStyle.Fill; path.Margin = new Padding(Px(3), 0, gap * 2, 0);
        top.Controls.Add(path, 0, 3); top.SetColumnSpan(path, 2);
        root.Controls.Add(top);

        // The language row (one radio button per language) lives in the info table, right under the MultiFix line.
        languageRow.Name = "LanguageBox"; languageRow.AutoSize = true; languageRow.AutoSizeMode = AutoSizeMode.GrowAndShrink; languageRow.FlowDirection = FlowDirection.LeftToRight;
        languageRow.WrapContents = false; languageRow.Margin = new Padding(0, 0, 0, Px(2));
        tips.SetToolTip(languageRow, "Game/Language - game text language (the DLLs in the TEXT folder); Auto = the game's own setting");
        // Edition / executable / MultiFix lines as an aligned key-value table.
        infoTable.Name = "InfoBox"; infoTable.AutoSize = true; infoTable.AutoSizeMode = AutoSizeMode.GrowAndShrink; infoTable.ColumnCount = 2; infoTable.Dock = DockStyle.Fill; infoTable.Margin = new Padding(0, 0, 0, gap * 2);
        infoTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); infoTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.Controls.Add(infoTable);

        root.Controls.Add(new Label { Text = "Options (live from RoadRashHD.ini)", AutoSize = true, Margin = new Padding(0, 0, 0, gap) });
        var rows = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, gap) };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        int r = 0;
        foreach (var option in Installer.Options)
        {
            var box = new CheckBox { Text = option.Label, Tag = option, AutoSize = true, Checked = option.Default, Anchor = AnchorStyles.Left, Margin = new Padding(Px(3), Px(2), Px(14), Px(2)) };
            box.Name = "Option_" + option.Section + "_" + option.Key;
            var hint = new Label { Text = option.Hint, AutoSize = true, ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Left, Margin = new Padding(0, Px(2), 0, Px(2)) };
            tips.SetToolTip(box, option.Section + "/" + option.Key + " - " + option.Hint);
            box.CheckedChanged += OptionChanged;
            boxes.Add(box);
            rows.Controls.Add(box, 0, r); rows.Controls.Add(hint, 1, r); ++r;
        }
        // FMV volume: a slider row under the check boxes.
        {
            var label = new Label { Text = "FMV Volume", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(Px(3), Px(4), Px(14), Px(2)) };
            videoVolume.Name = "VideoVolumeBar"; videoVolume.Minimum = 0; videoVolume.Maximum = 100; videoVolume.TickFrequency = 10; videoVolume.SmallChange = 5; videoVolume.LargeChange = 10;
            videoVolume.Value = 100; videoVolume.AutoSize = false; videoVolume.Size = new Size(Px(180), Px(26)); videoVolume.Margin = new Padding(0, 0, Px(8), 0);
            videoVolumeText.Name = "VideoVolumeText"; videoVolumeText.Text = "100 %"; videoVolumeText.AutoSize = true; videoVolumeText.Anchor = AnchorStyles.Left; videoVolumeText.MinimumSize = new Size(Px(44), 0);
            var line = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
            line.Controls.Add(videoVolume); line.Controls.Add(videoVolumeText);
            var hint = new Label { Text = "Volume of the intro and story videos (.avi and .mp4)", AutoSize = true, ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Left, Margin = new Padding(0, Px(2), 0, Px(2)) };
            tips.SetToolTip(videoVolume, "Game/VideoVolume - volume of all videos, 0-100 % of the game's volume");
            videoVolume.ValueChanged += VideoVolumeChanged;
            rows.Controls.Add(label, 0, r); rows.Controls.Add(line, 1, r); ++r;
            rows.Controls.Add(hint, 1, r); ++r;
        }
        root.Controls.Add(rows);

        status.Name = "StatusLabel"; status.AutoSize = true; status.MaximumSize = new Size(wrap, 0); status.ForeColor = SystemColors.GrayText; status.Margin = new Padding(0, 0, 0, gap);
        root.Controls.Add(status);

        var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(Px(3), 0, 0, 0) };
        Button[] all = { play, install, restore, new Button { Name = "CloseButton", Text = "Close", DialogResult = DialogResult.Cancel } };
        play.Name = "PlayButton"; play.Text = "Play";
        install.Name = "InstallButton"; install.Text = "Install / Update";
        restore.Name = "RestoreButton"; restore.Text = "Restore original";
        foreach (var b in all) { b.AutoSize = true; b.AutoSizeMode = AutoSizeMode.GrowAndShrink; b.Padding = new Padding(Px(10), Px(4), Px(10), Px(4)); b.Margin = new Padding(0, 0, gap, 0); buttons.Controls.Add(b); }
        play.Font = new Font(Font, FontStyle.Bold);
        root.Controls.Add(buttons);
        var about = new Label { Name = "AboutLabel", Text = "Road Rash\u2122: 30th Anniversary Edition - fixes, widescreen and a Direct3D renderer. MultiFix loads through ddraw.dll; RoadRash95.exe is never modified.", AutoSize = true, MaximumSize = new Size(wrap, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, gap * 2, 0, 0) };
        root.Controls.Add(about);
        var footer = new Label { Name = "FooterLabel", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, gap * 2, 0, 0),
            Text = "Original author: OBLPlife   |   Second author: magoowa   |   " + ModInfo.Name + " v" + ModInfo.Version };
        root.Controls.Add(footer);
        CancelButton = all[3];
        // The window runs modelessly (Application.Run), where a button's DialogResult does not close the form.
        all[3].Click += delegate { Close(); };
        Controls.Add(root);

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
                if (dialog.ShowDialog(this) == DialogResult.OK) SetFolder(dialog.SelectedPath);
        };
        play.Click += delegate { Play(); };
        install.Click += delegate { Run(false); };
        restore.Click += delegate { Run(true); };
        // Changes made to the ini outside this window (or by the game folder owner) show up when the window is focused again.
        Activated += delegate { if (!string.IsNullOrEmpty(path.Text)) { LoadOptions(path.Text); RefreshInfo(); } };
        if (installs.Items.Count > 0) installs.SelectedIndex = 0;
        RefreshInfo();
    }

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
            status.Text = "Saved: Game/VideoVolume = " + videoVolume.Value + " (takes effect the next time a video plays)";
        else
            status.Text = "RoadRashHD.ini does not exist yet; press Install / Update first to set the video volume.";
    }
    void LanguageChanged(object sender, EventArgs e)
    {
        var radio = sender as RadioButton;
        if (loading || radio == null || !radio.Checked) return;
        string name = (string)radio.Tag;
        if (Installer.SaveLanguage(path.Text, name))
            status.Text = "Saved: Game/Language = " + name + " (takes effect the next time the game starts)";
        else
            status.Text = "RoadRashHD.ini does not exist yet; press Install / Update first to choose a language.";
    }
    void OptionChanged(object sender, EventArgs e)
    {
        UpdateDependencies();
        if (loading) return;
        var box = (CheckBox)sender; var option = (PatchOption)box.Tag;
        if (Installer.SaveOption(path.Text, option, box.Checked))
            status.Text = "Saved: " + option.Section + "/" + option.Key + " = " + (box.Checked ? "1" : "0") + " (takes effect the next time the game starts)";
        else
            status.Text = "RoadRashHD.ini does not exist yet; this choice is applied when you press Install / Update.";
    }
    void RefreshInfo()
    {
        infoTable.SuspendLayout();
        infoTable.Controls.Clear(); infoTable.RowStyles.Clear(); infoTable.RowCount = 0;
        foreach (string line in Installer.Describe(path.Text).Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = line.IndexOf(": ", StringComparison.Ordinal);
            int row = infoTable.RowCount++;
            infoTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (colon > 0 && colon < 16)
            {
                infoTable.Controls.Add(new Label { Text = line.Substring(0, colon + 1), AutoSize = true, Margin = new Padding(0, 0, Px(18), Px(2)) }, 0, row);
                infoTable.Controls.Add(new Label { Text = line.Substring(colon + 2), AutoSize = true, MaximumSize = new Size(Px(600), 0), Margin = new Padding(0, 0, 0, Px(2)) }, 1, row);
            }
            else
            {
                var single = new Label { Text = line, AutoSize = true, MaximumSize = new Size(Px(600), 0), Margin = new Padding(0, 0, 0, Px(2)) };
                infoTable.Controls.Add(single, 0, row); infoTable.SetColumnSpan(single, 2);
            }
        }
        // Language, directly under the MultiFix line.
        int languageIndex = infoTable.RowCount++;
        infoTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        infoTable.Controls.Add(new Label { Text = "Language:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, Px(18), Px(2)) }, 0, languageIndex);
        infoTable.Controls.Add(languageRow, 1, languageIndex);
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
