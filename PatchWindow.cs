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
    readonly Label info = new Label(), status = new Label();
    readonly List<CheckBox> boxes = new List<CheckBox>();
    readonly ToolTip tips = new ToolTip();
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

        var title = new Label { Text = ModInfo.Name + "  v" + ModInfo.Version, Font = new Font("Segoe UI Semibold", 13F), AutoSize = true, Margin = new Padding(0, 0, 0, gap) };
        var subtitle = new Label { Text = "Road Rash™: 30th Anniversary Edition - fixes, widescreen and Direct3D renderer. Loads through ddraw.dll; RoadRash95.exe is never modified.", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, gap * 2) };
        root.Controls.Add(title); root.Controls.Add(subtitle);

        root.Controls.Add(new Label { Text = "Detected installations", AutoSize = true, Margin = new Padding(0, 0, 0, 2) });
        var pick = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, gap) };
        pick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); pick.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        installs.Name = "InstallsList"; installs.DropDownStyle = ComboBoxStyle.DropDownList; installs.Dock = DockStyle.Fill; installs.Margin = new Padding(0, 0, gap, 0);
        browse.Name = "BrowseButton"; browse.Text = "Browse..."; browse.AutoSize = true; browse.Margin = new Padding(0);
        pick.Controls.Add(installs, 0, 0); pick.Controls.Add(browse, 1, 0);
        root.Controls.Add(pick);

        root.Controls.Add(new Label { Text = "Game folder", AutoSize = true, Margin = new Padding(0, 0, 0, 2) });
        path.Name = "FolderBox"; path.Dock = DockStyle.Fill; path.Margin = new Padding(0, 0, 0, gap);
        root.Controls.Add(path);

        info.Name = "InfoBox"; info.AutoSize = true; info.MaximumSize = new Size(wrap, 0); info.Margin = new Padding(0, 0, 0, gap * 2);
        root.Controls.Add(info);

        var group = new GroupBox { Text = "Options (live from RoadRashHD.ini)", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Padding = new Padding(Px(8)), Margin = new Padding(0, 0, 0, gap) };
        var rows = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Dock = DockStyle.Fill };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        int r = 0;
        foreach (var option in Installer.Options)
        {
            var box = new CheckBox { Text = option.Label, Tag = option, AutoSize = true, Checked = option.Default, Anchor = AnchorStyles.Left, Margin = new Padding(0, Px(2), Px(14), Px(2)) };
            box.Name = "Option_" + option.Section + "_" + option.Key;
            var hint = new Label { Text = option.Hint, AutoSize = true, ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Left, Margin = new Padding(0, Px(2), 0, Px(2)) };
            tips.SetToolTip(box, option.Section + "/" + option.Key + " - " + option.Hint);
            box.CheckedChanged += OptionChanged;
            boxes.Add(box);
            rows.Controls.Add(box, 0, r); rows.Controls.Add(hint, 1, r); ++r;
        }
        group.Controls.Add(rows);
        root.Controls.Add(group);

        status.Name = "StatusLabel"; status.AutoSize = true; status.MaximumSize = new Size(wrap, 0); status.ForeColor = SystemColors.GrayText; status.Margin = new Padding(0, 0, 0, gap);
        root.Controls.Add(status);

        var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0) };
        Button[] all = { play, install, restore, new Button { Name = "CloseButton", Text = "Close", DialogResult = DialogResult.Cancel } };
        play.Name = "PlayButton"; play.Text = "Play";
        install.Name = "InstallButton"; install.Text = "Install / Update";
        restore.Name = "RestoreButton"; restore.Text = "Restore original";
        foreach (var b in all) { b.AutoSize = true; b.AutoSizeMode = AutoSizeMode.GrowAndShrink; b.Padding = new Padding(Px(10), Px(4), Px(10), Px(4)); b.Margin = new Padding(0, 0, gap, 0); buttons.Controls.Add(b); }
        play.Font = new Font(Font, FontStyle.Bold);
        root.Controls.Add(buttons);
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
        try { foreach (var box in boxes) { bool v = Installer.ReadOption(folder, (PatchOption)box.Tag); if (box.Checked != v) box.Checked = v; } }
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
        info.Text = Installer.Describe(path.Text);
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
