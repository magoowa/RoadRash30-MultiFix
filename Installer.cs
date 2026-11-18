// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("RoadRash30-MultiFix")]
[assembly: AssemblyDescription("Native HD, aspect ratio support, texture filtering and fixes for the GOG, EA app and Steam English builds. Loads through ddraw.dll; RoadRash95.exe is only changed by the optional 4 GB (Large Address Aware) switch, with a backup.")]
[assembly: AssemblyVersion(ModInfo.Version + ".0")]
[assembly: AssemblyFileVersion(ModInfo.Version + ".0")]

// One game installation found on this computer (or chosen by the user).
internal sealed class GameInstall
{
    public string Path, Source;
    public override string ToString() { return Source + " - " + Path; }
}

// One switch stored in RoadRashHD.ini.
internal sealed class PatchOption
{
    public string Section, Key, Label, Hint, Category;
    public bool Default;
    // Inverted: the check box means the opposite of the ini value (checked = ini value 0).
    public bool Inverted;
    public PatchOption(string section, string key, bool def, string label, string hint, bool inverted = false)
    { Section = section; Key = key; Default = def; Label = label; Hint = hint; Inverted = inverted; }
}

// One 0-100 setting of RoadRashHD.ini that is shown as a slider.
internal sealed class PatchSlider
{
    public string Section, Key, Label, Hint, Category;
    public int Min, Max, Default;
    public PatchSlider(string section, string key, string label, string hint, int min, int max, int def, string category)
    { Section = section; Key = key; Label = label; Hint = hint; Min = min; Max = max; Default = def; Category = category; }
}

internal static class Installer
{
    public const string Title = ModInfo.Name + " " + ModInfo.Version + ModInfo.BuildSuffix;
    const string ExeName = "RoadRash95.exe", IniName = "RoadRashHD.ini";
    // Earlier versions kept a backup folder with a state file; it is read once (to bring a saved original back) and removed.
    const string LegacyBackupFolder = "ResolutionPatch_Backup";
    // A different file that was in the way is kept next to the new one, e.g. ddraw.dll.multifix-backup.
    const string BackupSuffix = ".multifix-backup";
    // Files this installer owns. Both are embedded in the installer.
    static readonly string[] Managed = { "ddraw.dll", "RoadRashMultiFix.dll" };

    // Executables the patch was verified with. Anything else is still allowed (the
    // renderer checks the game code itself and does nothing on a mismatch).
    static readonly Dictionary<string, string> KnownExecutables = new Dictionary<string, string>
    {
        { "9e40e25737149ab095061b7154797a35495faea97644fafc5a1e616f1400c968",
          "GOG (60184028392664413) and Steam (26105967)" },
        // the same executable with the 4 GB (Large Address Aware) flag set by the launcher
        { "598c367d6a97a45aacf821a0b499039e7a367d293387e7fd64455251062ecbf2",
          "GOG (60184028392664413) and Steam (26105967), 4 GB patched" }
    };

    // Every switch of RoadRashHD.ini that makes sense to a player; each is shown on its own row, one line each.
    public static readonly PatchOption[] Options =
    {
        new PatchOption("Input", "AsyncControllerScan", true, "Stutter Fix", "Scans for controllers in the background; removes the periodic 165 ms freeze"),
        new PatchOption("Game", "FullRateObjects", true, "Sprite Position Interpolation", "Updates distant riders, cars and pedestrians every frame instead of every 2-8 frames"),
        new PatchOption("Text", "FixEncoding", true, "Fix Text Encoding", "Fixes Unicode text the game misreads (for example the trademark sign shown as \u00e2\u201e\u00a2)"),
        new PatchOption("Graphics", "D3D11", true, "Direct3D 11 Hardware Renderer", "Draws and presents the 3D scene on the GPU; falls back to the CPU renderer if unavailable"),
        new PatchOption("Graphics", "TextureFilter", true, "Texture Filtering (Direct3D Only)", "Bilinear filtering of the game's 3D environment (road, buildings, sky); never menus or text"),
        new PatchOption("Graphics", "VSync", false, "VSync", "Waits for the monitor refresh when presenting (off = lowest latency)"),
        new PatchOption("Performance", "HighPerformance", true, "High Performance Mode", "Turns off Windows power throttling for the game, 1 ms timer, slightly higher priority (helps integrated GPUs and laptops)"),
        new PatchOption("Performance", "LowLatency", true, "iGPU Performance Boost", "Turns off Low-Latency Presentation: faster on battery-powered Intel iGPUs", true),
        new PatchOption("Window", "WideWindow", true, "Widescreen Game", "Plays the whole game in widescreen (wide field of view); off = the original 4:3 picture with black bars"),
        new PatchOption("Window", "AltEnterFullscreen", true, "Alt+Enter / F11 Fullscreen Toggle", "Switches between the window and borderless fullscreen"),
        new PatchOption("Input", "SymmetricSteering", true, "Symmetric Analog Steering", "Controller stick turns right as gently as left (the game made right about twice as sensitive)"),
        new PatchOption("Input", "KeyboardAndPad", true, "Keyboard + Controller Together", "Both work at the same time, whatever device the profile has selected"),
        new PatchOption("Input", "PadDialogs", true, "Controller Answers Dialogs", "A = OK / Yes and B = Cancel / No in the game's message boxes (for example when quitting)"),
        new PatchOption("Game", "FixBikeColors", true, "Fix Bike Colors", "The Rat and Super bikes have swapped colours in the race; they now match the showroom"),
        new PatchOption("Game", "SpriteFixes", true, "Sprite Fixes", "Fixes the damaged punch shadow and lowers the buildings that hover above the ground (red barn, brown shop, white house)"),
        new PatchOption("Window", "AutoHideCursor", true, "Auto-Hide Mouse Cursor", "Hidden at start and after 3 s without mouse movement; returns when you move the mouse"),
        new PatchOption("Window", "StartFullscreen", true, "Start in Fullscreen", "Opens the game in borderless fullscreen"),
        new PatchOption("Input", "DualSense", false, "DualSense Support", "Bluetooth reports and d-pad steering and menus for a DualSense or DualShock over HID"),
        new PatchOption("Window", "DualMonitorUX", false, "Dual Monitor UX Improvements", "The mouse is not confined to the game window and the game does not minimize when you click another monitor"),
        new PatchOption("Audio", "PedestrianVoiceRules", true, "New Pedestrian Voices", "Bystanders, bikers, cops and others shout with a chosen pool of voices; flag girls and people at the start react too"),
        new PatchOption("Game", "SkipIntro", false, "Skip Intro Videos", "Skips the studio logos and the intro movie (menu music still starts)"),
        new PatchOption("Audio", "MusicWavFiles", false, "WAV Music Replacement", "Plays MUSIC\\NAME.WAV instead of NAME.RRA when it exists (any PCM WAV; 44.1 kHz and 48 kHz are fine)"),
        new PatchOption("Audio", "MusicPlaylist", false, "Music Playlist", "Every WAV in AUDIO\\MUSIC\\Playlist is played as game music, in random order (needs WAV Music Replacement)"),
        new PatchOption("Audio", "MidiVolumeGuard", true, "Muted Sound Fix", "MIDI music follows the Sound Mix slider (0 = silent); the game is not silenced in the Windows mixer"),
        new PatchOption("Game", "DistanceToFinish", false, "Distance to Finish", "The HUD distance counts down to the finish line instead of up from the start"),
        new PatchOption("Game", "SmoothRoad", true, "Smooth Road", "Removes the half-pixel snapping of the road and adds back the fractions the game drops, so the road and its edges do not jump or pulse"),
    };

    // The launcher groups the options in tabs. Stability comes first.
    public static readonly string[] Categories = { "Stability and QoL", "Graphics", "Display", "Audio", "Controls" };
    static readonly Dictionary<string, string> CategoryOfKey = new Dictionary<string, string>
    {
        { "Input/AsyncControllerScan", "Stability and QoL" }, { "Game/FullRateObjects", "Stability and QoL" }, { "Performance/HighPerformance", "Stability and QoL" }, { "Performance/LowLatency", "Stability and QoL" },
        { "Graphics/D3D11", "Graphics" }, { "Graphics/TextureFilter", "Graphics" }, { "Graphics/VSync", "Graphics" }, { "Game/FixBikeColors", "Graphics" },
        { "Game/SpriteFixes", "Graphics" }, { "Game/SmoothRoad", "Graphics" },
        { "Window/WideWindow", "Display" }, { "Window/StartFullscreen", "Display" }, { "Window/AltEnterFullscreen", "Display" }, { "Window/AutoHideCursor", "Display" }, { "Window/DualMonitorUX", "Display" },
        { "Audio/PedestrianVoiceRules", "Audio" }, { "Audio/MidiVolumeGuard", "Audio" }, { "Audio/MusicWavFiles", "Audio" }, { "Audio/MusicPlaylist", "Audio" },
        { "Input/SymmetricSteering", "Controls" }, { "Input/KeyboardAndPad", "Controls" }, { "Input/PadDialogs", "Controls" }, { "Input/DualSense", "Controls" },
        { "Game/SkipIntro", "Stability and QoL" }, { "Game/DistanceToFinish", "Stability and QoL" }, { "Text/FixEncoding", "Stability and QoL" }
    };
    // Settings shown as sliders (in the tab of their category).
    public static readonly PatchSlider[] Sliders =
    {
        new PatchSlider("Audio", "PedestrianLoudness", "Pedestrian Loudness", "Voices and footsteps of pedestrians: 0 = the game's own levels, 100 = the full boost of earlier versions", 0, 100, 67, "Audio"),
        new PatchSlider("Game", "VideoVolume", "FMV Volume", "Volume of the intro and story videos (.avi and .mp4)", 0, 100, 100, "Audio")
    };
    static Installer()
    {
        foreach (var o in Options)
        {
            string category;
            o.Category = CategoryOfKey.TryGetValue(o.Section + "/" + o.Key, out category) ? category : "Stability and QoL";
        }
    }
    public static int ReadSlider(string folder, PatchSlider slider)
    {
        string ini = Path.Combine(folder ?? "", IniName);
        if (!File.Exists(ini)) return slider.Default;
        return Math.Max(slider.Min, Math.Min(slider.Max, (int)GetPrivateProfileInt(slider.Section, slider.Key, slider.Default, ini)));
    }
    public static bool SaveSlider(string folder, PatchSlider slider, int value)
    {
        try
        {
            string ini = Path.Combine(folder ?? "", IniName);
            return File.Exists(ini) && WritePrivateProfileString(slider.Section, slider.Key, Math.Max(slider.Min, Math.Min(slider.Max, value)).ToString(), ini);
        }
        catch (Exception) { return false; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern uint GetPrivateProfileInt(string section, string key, int def, string file);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern uint GetPrivateProfileString(string section, string key, string def, StringBuilder buffer, uint size, string file);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool WritePrivateProfileString(string section, string key, string value, string file);

    sealed class Game
    {
        public string Root;
        public Game(string root) { Root = root; }
        public string LegacyFolder { get { return Path.Combine(Root, LegacyBackupFolder); } }
        public string BackupOf(int index) { return Path.Combine(Root, Managed[index] + BackupSuffix); }
    }
    sealed class Operation
    {
        public string Path, Temporary;
        public byte[] Before, After;
    }

    // ------------------------------------------------------------------ detection

    static string Hash(byte[] data)
    {
        if (data == null) return "-";
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
    }
    static string HashFile(string path)
    {
        try { using (var sha = SHA256.Create()) using (var f = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant(); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
    static string RegString(RegistryHive hive, RegistryView view, string key, string name)
    {
        try
        {
            using (var b = RegistryKey.OpenBaseKey(hive, view))
            using (var k = b.OpenSubKey(key))
            { var v = k == null ? null : k.GetValue(name); return v == null ? null : v.ToString(); }
        }
        catch (Exception) { return null; }
    }
    static void AddIfGame(List<GameInstall> list, string source, string folder)
    {
        if (string.IsNullOrEmpty(folder)) return;
        try
        {
            folder = Path.GetFullPath(folder.Trim().Trim('"')).TrimEnd('\\');
            if (!File.Exists(Path.Combine(folder, ExeName))) return;
            foreach (var g in list) if (string.Equals(g.Path, folder, StringComparison.OrdinalIgnoreCase)) return;
            list.Add(new GameInstall { Path = folder, Source = source });
        }
        catch (Exception) { }
    }
    // Look one level below a store's games folder for a Road Rash installation.
    static void ScanParent(List<GameInstall> list, string source, string parent)
    {
        try
        {
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return;
            foreach (string dir in Directory.GetDirectories(parent)) AddIfGame(list, source, dir);
        }
        catch (Exception) { }
    }
    public static List<GameInstall> Detect()
    {
        var list = new List<GameInstall>();
        // 1. The folder this installer was started from.
        AddIfGame(list, "This folder", Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
        // 2. GOG Galaxy / GOG installer registry entries (game id 2141922451).
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            AddIfGame(list, "GOG", RegString(RegistryHive.LocalMachine, view, @"SOFTWARE\GOG.com\Games\2141922451", "path"));
        string pf = Environment.GetEnvironmentVariable("ProgramFiles"), pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        foreach (string parent in new[] { @"C:\GOG Games", @"D:\GOG Games", Path.Combine(pf86 ?? "", @"GOG Galaxy\Games"), Path.Combine(pf ?? "", @"GOG Galaxy\Games"), Path.Combine(pf86 ?? "", "GOG Games") })
            ScanParent(list, "GOG", parent);
        // 3. Steam libraries.
        var steamRoots = new List<string>();
        string steam = RegString(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Steam", "SteamPath");
        if (steam == null) steam = RegString(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Valve\Steam", "InstallPath");
        if (steam != null) steamRoots.Add(steam.Replace('/', '\\'));
        foreach (string root in steamRoots.ToArray())
        {
            string vdf = Path.Combine(root, @"steamapps\libraryfolders.vdf");
            try
            {
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        steamRoots.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
            catch (Exception) { }
        }
        foreach (string root in steamRoots) ScanParent(list, "Steam", Path.Combine(root, @"steamapps\common"));
        // 4. EA app / EA Games / Origin.
        foreach (string parent in new[] { Path.Combine(pf ?? "", "EA Games"), Path.Combine(pf86 ?? "", "EA Games"), Path.Combine(pf86 ?? "", "Origin Games"),
                                          Path.Combine(pf ?? "", "Origin Games"), @"C:\EA Games", @"D:\EA Games" })
            ScanParent(list, "EA", parent);
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            foreach (string name in new[] { "Install Dir", "InstallDir", "Path" })
                AddIfGame(list, "EA", RegString(RegistryHive.LocalMachine, view, @"SOFTWARE\Electronic Arts\RoadRash 95", name));
        // Tidy the label of installs found through generic locations.
        foreach (var g in list)
        {
            if (File.Exists(Path.Combine(g.Path, "goggame-2141922451.info"))) g.Source = "GOG";
            else if (g.Path.IndexOf("steamapps", StringComparison.OrdinalIgnoreCase) >= 0) g.Source = "Steam";
            else if (g.Path.IndexOf("EA Games", StringComparison.OrdinalIgnoreCase) >= 0 || g.Path.IndexOf("Origin Games", StringComparison.OrdinalIgnoreCase) >= 0) g.Source = "EA";
        }
        return list;
    }

    // Human-readable summary of a folder: edition, executable version, patch status.
    public static string Describe(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return "Choose the game folder (the one containing RoadRash95.exe).";
        string exe = Path.Combine(folder, ExeName);
        if (!File.Exists(exe)) return "RoadRash95.exe was not found in this folder.";
        var sb = new StringBuilder();
        string edition = File.Exists(Path.Combine(folder, "goggame-2141922451.info")) ? "GOG"
            : folder.IndexOf("steamapps", StringComparison.OrdinalIgnoreCase) >= 0 ? "Steam"
            : (folder.IndexOf("EA Games", StringComparison.OrdinalIgnoreCase) >= 0 || folder.IndexOf("Origin Games", StringComparison.OrdinalIgnoreCase) >= 0) ? "EA" : "unknown store";
        sb.Append("Edition: ").Append(edition).Append("\r\n");
        string hash = HashFile(exe), known;
        string fileVersion = "";
        try { fileVersion = FileVersionInfo.GetVersionInfo(exe).FileVersion; } catch (Exception) { }
        // A known executable with the 4 GB flag set (by this launcher or by hand) is recognised by the hash of the file with that one flag cleared.
        string unflagged = null;
        try
        {
            byte[] bytes = File.ReadAllBytes(exe);
            int at = CharacteristicsOffset(bytes);
            if (at >= 0 && (BitConverter.ToUInt16(bytes, at) & LaaFlag) != 0) { bytes[at] &= 0xdf; unflagged = Hash(bytes); }
        }
        catch (Exception) { }
        if (hash != null && KnownExecutables.TryGetValue(hash, out known)) sb.Append("Executable: ").Append(known).Append(" (verified)\r\n");
        else if (unflagged != null && KnownExecutables.TryGetValue(unflagged, out known)) sb.Append("Executable: ").Append(known).Append(", 4 GB patched (verified)\r\n");
        else sb.Append("Executable: not on the verified list (SHA-256 ").Append(hash == null ? "unreadable" : hash.Substring(0, 12) + "...")
               .Append(fileVersion != "" ? ", file version " + fileVersion : "").Append(")\r\n");
        sb.Append("MultiFix: ").Append(Status(folder).Describe());
        return sb.ToString();
    }
    // What is in the game folder: our ddraw.dll proxy and the renderer DLL, and the renderer's version.
    public sealed class PatchStatus
    {
        public bool ProxyOurs, ProxyPresent, RendererPresent, RendererCurrent;
        public string RendererVersion = "", RendererBuild = "";
        public bool Installed { get { return ProxyOurs && RendererPresent; } }
        public bool Outdated { get { return Installed && !RendererCurrent; } }
        public string Describe()
        {
            if (Installed)
                return RendererCurrent ? "installed, DLL version " + RendererVersion + (RendererBuild != "" ? " test " + RendererBuild : "") + " (up to date)"
                    : "installed, DLL version " + (RendererVersion != "" ? RendererVersion : "unknown") + " (this launcher has " + ModInfo.Version + " - press Install / Update)";
            if (ProxyPresent && !ProxyOurs) return "NOT installed - the game folder has a different ddraw.dll (another wrapper?); Install backs it up";
            if (ProxyOurs || RendererPresent) return "partially installed (a MultiFix file is missing) - press Install / Update";
            return "NOT installed";
        }
    }
    public static PatchStatus Status(string folder)
    {
        var s = new PatchStatus();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return s;
        try
        {
            string proxy = Path.Combine(folder, "ddraw.dll"), renderer = Path.Combine(folder, "RoadRashMultiFix.dll");
            s.ProxyPresent = File.Exists(proxy);
            s.ProxyOurs = s.ProxyPresent && (HashFile(proxy) == Hash(Resource("ddraw.dll")) || IsOurProxy(File.ReadAllBytes(proxy)) || Directory.Exists(Path.Combine(folder, LegacyBackupFolder)));
            s.RendererPresent = File.Exists(renderer);
            if (s.RendererPresent)
            {
                s.RendererCurrent = HashFile(renderer) == Hash(Resource("RoadRashMultiFix.dll"));
                byte[] rendererBytes = File.ReadAllBytes(renderer);
                s.RendererVersion = TagVersion(rendererBytes);
                s.RendererBuild = TagValue(rendererBytes, "RRHD_BUILD_TAG:");
                if (s.RendererCurrent && s.RendererVersion == "") s.RendererVersion = ModInfo.Version;
            }
        }
        catch (Exception) { }
        return s;
    }
    // The renderer DLL carries "RRHD_VERSION_TAG:<version>" followed by a NUL byte.
    static string TagVersion(byte[] data) { return TagValue(data, "RRHD_VERSION_TAG:"); }
    static string TagValue(byte[] data, string prefix)
    {
        byte[] tag = Encoding.ASCII.GetBytes(prefix);
        for (int i = 0; i + tag.Length < data.Length; ++i)
        {
            int k = 0;
            while (k < tag.Length && data[i + k] == tag[k]) ++k;
            if (k != tag.Length) continue;
            int end = i + tag.Length;
            while (end < data.Length && data[end] != 0 && end - i < 64) ++end;
            return Encoding.ASCII.GetString(data, i + tag.Length, end - i - tag.Length);
        }
        return "";
    }
    // Game language: the text DLLs in the game's TEXT folder. "Auto" keeps the game's own choice (registry).
    public static readonly string[] LanguageNames = { "English", "French", "German", "Italian", "Spanish" };
    static readonly string[] LanguageFiles = { "ENU.DLL", "FRA.DLL", "DEU.DLL", "ITA.DLL", "ESP.DLL" };
    public static List<string> AvailableLanguages(string folder)
    {
        var list = new List<string> { "Auto" };
        for (int i = 0; i < LanguageNames.Length; ++i)
            if (File.Exists(Path.Combine(folder ?? "", "TEXT", LanguageFiles[i]))) list.Add(LanguageNames[i]);
        return list;
    }
    public static string ReadLanguage(string folder)
    {
        string ini = Path.Combine(folder ?? "", IniName);
        if (!File.Exists(ini)) return "Auto";
        var value = new StringBuilder(32);
        GetPrivateProfileString("Game", "Language", "Auto", value, 32, ini);
        return value.ToString();
    }
    // Video volume (0-100) in [Game] VideoVolume; 100 when the ini does not exist yet.
    public static int ReadVideoVolume(string folder)
    {
        string ini = Path.Combine(folder ?? "", IniName);
        if (!File.Exists(ini)) return 100;
        return Math.Max(0, Math.Min(100, (int)GetPrivateProfileInt("Game", "VideoVolume", 100, ini)));
    }
    public static bool SaveVideoVolume(string folder, int percent)
    {
        try
        {
            string ini = Path.Combine(folder ?? "", IniName);
            return File.Exists(ini) && WritePrivateProfileString("Game", "VideoVolume", Math.Max(0, Math.Min(100, percent)).ToString(), ini);
        }
        catch (Exception) { return false; }
    }
    public static bool SaveLanguage(string folder, string name)
    {
        try
        {
            string ini = Path.Combine(folder ?? "", IniName);
            return File.Exists(ini) && WritePrivateProfileString("Game", "Language", name, ini);
        }
        catch (Exception) { return false; }
    }
    // ------------------------------------------------------------------ links, netplay, updates

    public const string ProjectUrl = "https://github.com/magoowa/RoadRash30-MultiFix";
    public const string OkPlayUrl = "https://github.com/Patrick-Working/okplay/";
    const string LatestReleaseApi = "https://api.github.com/repos/magoowa/RoadRash30-MultiFix/releases/latest";
    public const string LatestReleaseUrl = ProjectUrl + "/releases/latest";

    // Netplay (OKPlay) works through a wsock32.dll in the game folder. Returns its datecode (yyyyMMMdd of the file),
    // or null when the DLL is missing.
    public static string NetplayDatecode(string folder)
    {
        try
        {
            string dll = Path.Combine(folder ?? "", "wsock32.dll");
            if (!File.Exists(dll)) return null;
            return File.GetLastWriteTime(dll).ToString("yyyyMMMdd", System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception) { return null; }
    }

    // "2.15.0", "v2.15.0" -> numbers; anything else -> null.
    static int[] ParseVersion(string text)
    {
        if (text == null) return null;
        var match = Regex.Match(text.Trim(), @"^[vV]?(\d+)\.(\d+)(?:\.(\d+))?");
        if (!match.Success) return null;
        return new[] { int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0 };
    }
    // True when latest is a higher version than current.
    public static bool IsNewer(string latest, string current)
    {
        int[] a = ParseVersion(latest), b = ParseVersion(current);
        if (a == null || b == null) return false;
        for (int i = 0; i < 3; ++i) if (a[i] != b[i]) return a[i] > b[i];
        return false;
    }
    // Reads tag_name and html_url from the GitHub "latest release" JSON.
    public static bool ParseLatestRelease(string json, out string version, out string url)
    {
        version = null; url = LatestReleaseUrl;
        if (string.IsNullOrEmpty(json)) return false;
        var tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
        if (!tag.Success || ParseVersion(tag.Groups[1].Value) == null) return false;
        version = tag.Groups[1].Value.TrimStart('v', 'V');
        var page = Regex.Match(json, "\"html_url\"\\s*:\\s*\"(https://github\\.com/magoowa/RoadRash30-MultiFix/[^\"]*)\"");
        if (page.Success) url = page.Groups[1].Value;
        return true;
    }
    // Asks GitHub for the latest release (blocking; call it from a worker thread). Null on any failure.
    public static string FetchLatestReleaseJson()
    {
        try
        {
            System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;   // TLS 1.2
            var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(LatestReleaseApi);
            request.UserAgent = "RoadRash30-MultiFix-launcher";
            request.Accept = "application/vnd.github+json";
            request.Timeout = 8000;
            using (var response = request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) return reader.ReadToEnd();
        }
        catch (Exception) { return null; }
    }

    // ------------------------------------------------------------------ netplay download (OKPlay by Oldkid.net)

    // Primary source (a redirect to the newest zip); the GitHub release of okplay is the fallback. Tests replace the list.
    internal static string[] NetplayUrls = { "https://oldkid.net/aoe2net/latest.php" };
    const string OkPlayTagsApi = "https://api.github.com/repos/Patrick-Working/okplay/tags";
    const string OkPlayReleaseApi = "https://api.github.com/repos/Patrick-Working/okplay/releases/latest";
    // What is taken from the zip (by file name, wherever it sits in the archive). readme.txt is saved as oldkid_netplay_readme.txt.
    static readonly string[] NetplayFiles = { "netcheck.exe", "okplay.ini", "wsock32.dll", "readme.txt" };
    const int NetplayMaxBytes = 25 * 1024 * 1024;

    static byte[] DownloadBytes(string url)
    {
        System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;   // TLS 1.2
        var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
        request.UserAgent = "RoadRash30-MultiFix-launcher";
        request.Timeout = 20000; request.ReadWriteTimeout = 20000; request.AllowAutoRedirect = true;
        using (var response = request.GetResponse())
        using (var stream = response.GetResponseStream())
        using (var memory = new MemoryStream())
        {
            var buffer = new byte[65536]; int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                memory.Write(buffer, 0, read);
                if (memory.Length > NetplayMaxBytes) throw new InvalidDataException("The download is larger than expected.");
            }
            return memory.ToArray();
        }
    }

    // Unpacks netcheck.exe, okplay.ini, wsock32.dll and readme.txt (as oldkid_netplay_readme.txt) into the game folder.
    // An okplay.ini that is already there is kept (it holds the player's settings). Returns the names written.
    public static List<string> ExtractNetplay(byte[] zipBytes, string folder)
    {
        var written = new List<string>();
        using (var archive = new System.IO.Compression.ZipArchive(new MemoryStream(zipBytes), System.IO.Compression.ZipArchiveMode.Read))
        {
            var found = new Dictionary<string, System.IO.Compression.ZipArchiveEntry>();
            foreach (var entry in archive.Entries)
            {
                string name = Path.GetFileName(entry.FullName.Replace('/', '\\')).ToLowerInvariant();   // folders in the zip are ignored
                if (Array.IndexOf(NetplayFiles, name) >= 0 && entry.Length > 0 && entry.Length <= NetplayMaxBytes && !found.ContainsKey(name)) found[name] = entry;
            }
            if (!found.ContainsKey("wsock32.dll")) throw new InvalidDataException("The archive does not contain wsock32.dll.");
            foreach (string name in NetplayFiles)
            {
                System.IO.Compression.ZipArchiveEntry entry;
                if (!found.TryGetValue(name, out entry)) continue;
                string target = name == "readme.txt" ? "oldkid_netplay_readme.txt" : name;
                string destination = Path.Combine(folder, target);
                if (name == "okplay.ini" && File.Exists(destination)) continue;   // keep the player's settings
                byte[] data;
                using (var input = entry.Open()) using (var memory = new MemoryStream()) { input.CopyTo(memory); data = memory.ToArray(); }
                if (name.EndsWith(".dll") || name.EndsWith(".exe"))
                    if (data.Length < 2 || data[0] != 0x4d || data[1] != 0x5a) throw new InvalidDataException(name + " is not a Windows program.");
                string temp = destination + ".download";
                File.WriteAllBytes(temp, data);
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(temp, destination);
                written.Add(target);
            }
        }
        return written;
    }

    // The installed netplay version: the readme that came with it ("Oldkid.net Online Play  v0.8.7") is saved next to wsock32.dll.
    public static string NetplayVersion(string folder)
    {
        try
        {
            string readme = Path.Combine(folder ?? "", "oldkid_netplay_readme.txt");
            if (!File.Exists(readme)) return null;
            using (var reader = new StreamReader(readme))
                for (int i = 0; i < 6; ++i)
                {
                    string line = reader.ReadLine();
                    if (line == null) break;
                    var match = Regex.Match(line, @"Online Play\s+v?(\d+\.\d+(?:\.\d+)?)");
                    if (match.Success) return match.Groups[1].Value;
                }
        }
        catch (Exception) { }
        return null;
    }
    // The newest netplay version: the highest version tag of github.com/Patrick-Working/okplay (the project has no downloadable
    // releases there; the built zip is on oldkid.net, which the download uses), or the name of the zip oldkid.net serves as "latest".
    // Blocking; call it from a worker thread. Null on any failure.
    public static string FetchLatestNetplayVersion()
    {
        if (Environment.GetEnvironmentVariable("RRMF_NO_UPDATE_CHECK") == "1") return null;
        string best = null;
        try
        {
            System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;
            var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(OkPlayTagsApi);
            request.UserAgent = "RoadRash30-MultiFix-launcher"; request.Accept = "application/vnd.github+json"; request.Timeout = 8000;
            using (var response = request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                foreach (Match tag in Regex.Matches(reader.ReadToEnd(), @"""name""\s*:\s*""[vV]?(\d+\.\d+(?:\.\d+)?)"""))
                    if (best == null || IsNewer(tag.Groups[1].Value, best)) best = tag.Groups[1].Value;
        }
        catch (Exception) { }
        if (best != null) return best;
        try
        {
            var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(NetplayUrls[0]);
            request.UserAgent = "RoadRash30-MultiFix-launcher"; request.Method = "HEAD"; request.AllowAutoRedirect = false; request.Timeout = 8000;
            using (var response = request.GetResponse())
            {
                var match = Regex.Match(response.Headers["Location"] ?? "", @"v(\d+\.\d+(?:\.\d+)?)\.zip", RegexOptions.IgnoreCase);
                if (match.Success) return match.Groups[1].Value;
            }
        }
        catch (Exception) { }
        return null;
    }

    // First zip asset of the latest okplay release on GitHub, or null (there may be none).
    static string OkPlayReleaseZipUrl()
    {
        if (Environment.GetEnvironmentVariable("RRMF_NO_UPDATE_CHECK") == "1") return null;   // tests never reach the network
        try
        {
            System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;
            var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(OkPlayReleaseApi);
            request.UserAgent = "RoadRash30-MultiFix-launcher"; request.Accept = "application/vnd.github+json"; request.Timeout = 10000;
            using (var response = request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                var match = Regex.Match(reader.ReadToEnd(), "\"browser_download_url\"\\s*:\\s*\"(https://[^\"]+\\.zip)\"");
                return match.Success ? match.Groups[1].Value : null;
            }
        }
        catch (Exception) { return null; }
    }

    // Downloads netplay (blocking; call from a worker thread). Returns where it came from, or null with the reason in error.
    public static string DownloadNetplay(string folder, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(folder) || !File.Exists(Path.Combine(folder, ExeName))) { error = "Choose the game folder first."; return null; }
        foreach (var process in Process.GetProcessesByName("RoadRash95")) { process.Dispose(); error = "Close Road Rash first."; return null; }
        var attempts = new List<string>(NetplayUrls);
        var reasons = new StringBuilder();
        for (int i = 0; i <= attempts.Count; ++i)
        {
            string url = i < attempts.Count ? attempts[i] : OkPlayReleaseZipUrl();   // GitHub release comes last
            if (url == null) { reasons.Append("GitHub: no release to download. "); break; }
            try
            {
                ExtractNetplay(DownloadBytes(url), folder);
                return new Uri(url).Host;
            }
            catch (Exception failure) { reasons.Append(new Uri(url).Host).Append(": ").Append(failure.Message).Append(' '); }
        }
        error = reasons.ToString().Trim();
        return null;
    }

    // Writes one option to RoadRashHD.ini immediately. Returns false when the ini does not exist yet.
    public static bool SaveOption(string folder, PatchOption option, bool value)
    {
        try
        {
            string ini = Path.Combine(folder ?? "", IniName);
            if (!File.Exists(ini)) return false;
            return WritePrivateProfileString(option.Section, option.Key, (value != option.Inverted) ? "1" : "0", ini);
        }
        catch (Exception) { return false; }
    }
    // Starts the game from the selected folder. Returns an error message, or null on success.
    public static string Launch(string folder)
    {
        try
        {
            string exe = Path.Combine(folder ?? "", ExeName);
            if (!File.Exists(exe)) return "RoadRash95.exe was not found in the selected folder.";
            var start = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = true };
            Process.Start(start);
            return null;
        }
        catch (Exception error) { return "Could not start the game: " + error.Message; }
    }
    public static bool ReadOption(string folder, PatchOption option)
    {
        string ini = Path.Combine(folder ?? "", IniName);
        if (!File.Exists(ini)) return option.Default;
        return (GetPrivateProfileInt(option.Section, option.Key, (option.Default != option.Inverted) ? 1 : 0, ini) != 0) != option.Inverted;
    }

    // ------------------------------------------------------------------ 4 GB (Large Address Aware)

    // The optional switch that changes RoadRash95.exe: one flag (IMAGE_FILE_LARGE_ADDRESS_AWARE, 0x20, in the Characteristics field of the PE
    // header). A 32-bit program gets 4 GB of address space instead of 2 GB on 64-bit Windows, which the game, the Direct3D driver and this patch share;
    // running out of it shows up as missing textures and a crash after a few miles. The original is kept as RoadRash95.exe.multifix-backup.
    public sealed class LaaStatus
    {
        public bool ExeFound, Valid, Patched, BackupExists, Is64BitWindows = Environment.Is64BitOperatingSystem;
        public string Error;
    }
    const ushort LaaFlag = 0x0020;
    static string ExeBackup(string folder) { return Path.Combine(folder, ExeName + BackupSuffix); }
    // Offset of the Characteristics field, or -1 when the bytes are not a 32-bit Windows executable.
    static int CharacteristicsOffset(byte[] exe)
    {
        if (exe == null || exe.Length < 0x100 || exe[0] != 0x4d || exe[1] != 0x5a) return -1;
        int pe = BitConverter.ToInt32(exe, 0x3c);
        if (pe < 0x40 || pe + 24 > exe.Length || exe[pe] != 'P' || exe[pe + 1] != 'E' || exe[pe + 2] != 0 || exe[pe + 3] != 0) return -1;
        if (BitConverter.ToUInt16(exe, pe + 4) != 0x14c) return -1;      // IMAGE_FILE_MACHINE_I386
        return pe + 22;
    }
    public static LaaStatus Laa(string folder)
    {
        var status = new LaaStatus();
        try
        {
            string exe = Path.Combine(folder ?? "", ExeName);
            status.ExeFound = File.Exists(exe);
            if (!status.ExeFound) return status;
            byte[] head;
            using (var file = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                head = new byte[Math.Min(4096, (int)file.Length)];
                int got = 0; while (got < head.Length) { int n = file.Read(head, got, head.Length - got); if (n <= 0) break; got += n; }
            }
            int at = CharacteristicsOffset(head);
            status.Valid = at >= 0;
            if (status.Valid) status.Patched = (BitConverter.ToUInt16(head, at) & LaaFlag) != 0;
            status.BackupExists = File.Exists(ExeBackup(folder));
        }
        catch (Exception error) { status.Error = error.Message; }
        return status;
    }
    // Turns the flag on (with a backup of the original) or off (the original comes back from the backup). Returns null, or the reason it failed.
    public static string SetLarge(string folder, bool enable)
    {
        try
        {
            if (string.IsNullOrEmpty(folder)) return "Choose the game folder first.";
            string exe = Path.Combine(folder, ExeName);
            if (!File.Exists(exe)) return "RoadRash95.exe was not found in this folder.";
            foreach (var process in Process.GetProcessesByName("RoadRash95")) { process.Dispose(); return "Close Road Rash first."; }
            if (enable && !Environment.Is64BitOperatingSystem) return "This Windows is 32-bit: the flag would have no effect.";
            byte[] bytes = File.ReadAllBytes(exe);
            int at = CharacteristicsOffset(bytes);
            if (at < 0) return "RoadRash95.exe is not a 32-bit Windows program that this switch understands.";
            ushort flags = BitConverter.ToUInt16(bytes, at);
            bool patched = (flags & LaaFlag) != 0;
            string backup = ExeBackup(folder);
            if (enable)
            {
                if (patched) return null;
                // the backup holds the unpatched file; it is replaced when the game's file is not the one it was made from (an update, a repair)
                if (!File.Exists(backup) || Hash(File.ReadAllBytes(backup)) != Hash(bytes))
                    File.WriteAllBytes(backup, bytes);
                var changed = (byte[])bytes.Clone();
                ushort value = (ushort)(flags | LaaFlag);
                changed[at] = (byte)(value & 0xff); changed[at + 1] = (byte)(value >> 8);
                ReplaceExe(exe, changed);
                return Laa(folder).Patched ? null : "The change did not stick (another program may have restored the file).";
            }
            if (!patched) return null;
            byte[] original = File.Exists(backup) ? File.ReadAllBytes(backup) : null;
            int backupAt = CharacteristicsOffset(original);
            bool sameFile = false;                                  // the backup is only used when it is this very file without the flag
            if (original != null && backupAt >= 0 && original.Length == bytes.Length && (BitConverter.ToUInt16(original, backupAt) & LaaFlag) == 0)
            {
                var cleared = (byte[])bytes.Clone();
                ushort clearedFlags = (ushort)(flags & ~LaaFlag);
                cleared[at] = (byte)(clearedFlags & 0xff); cleared[at + 1] = (byte)(clearedFlags >> 8);
                sameFile = Hash(cleared) == Hash(original);
            }
            if (sameFile)
            {
                ReplaceExe(exe, original);
                try { File.Delete(backup); } catch (Exception) { }
            }
            else
            {
                var changed = (byte[])bytes.Clone();                    // no usable backup: clear the one flag
                ushort value = (ushort)(flags & ~LaaFlag);
                changed[at] = (byte)(value & 0xff); changed[at + 1] = (byte)(value >> 8);
                ReplaceExe(exe, changed);
            }
            return Laa(folder).Patched ? "The change did not stick (another program may have restored the file)." : null;
        }
        catch (UnauthorizedAccessException) { return "The game folder is not writable. Run this launcher as administrator (right-click, Run as administrator)."; }
        catch (Exception error) { return error.Message; }
    }
    public static string LastAttributeWarning;
    static void ReplaceExe(string exe, byte[] bytes)
    {
        var attributes = File.GetAttributes(exe);
        if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(exe, attributes & ~FileAttributes.ReadOnly);
        string temporary = null;
        try
        {
            temporary = Stage(exe, bytes);
            File.Replace(temporary, exe, null);
        }
        catch { if (temporary != null) Cleanup(temporary); throw; }
        finally
        {
            try { File.SetAttributes(exe, attributes); } catch (Exception restore) { LastAttributeWarning = "The original file attributes of RoadRash95.exe could not be restored: " + restore.Message; }   // read-only comes back on every path
        }
    }

    // ------------------------------------------------------------------ file operations

    static byte[] Read(string path) { return File.Exists(path) ? File.ReadAllBytes(path) : null; }
    static bool Same(byte[] a, byte[] b) { return Hash(a) == Hash(b); }
    static byte[] Resource(string name)
    {
        using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        using (var destination = new MemoryStream())
        {
            if (source == null) throw new InvalidDataException("The installer is missing an embedded file: " + name);
            source.CopyTo(destination); return destination.ToArray();
        }
    }
    static void Cleanup(string path)
    {
        if (path == null) return;
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    static string Stage(string path, byte[] bytes)
    {
        string temporary = Path.Combine(Path.GetDirectoryName(path), ".rrhd-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes, 0, bytes.Length); file.Flush(true);
            }
            return temporary;
        }
        catch { Cleanup(temporary); throw; }
    }
    static bool Contains(byte[] data, string text)
    {
        if (data == null) return false;
        byte[] needle = Encoding.ASCII.GetBytes(text);
        for (int i = 0; i + needle.Length <= data.Length; ++i)
        {
            int k = 0;
            while (k < needle.Length && data[i + k] == needle[k]) ++k;
            if (k == needle.Length) return true;
        }
        return false;
    }
    // Our files carry a tag: the proxy "RRMF_PROXY_TAG", the renderer "RRHD_VERSION_TAG:".
    static bool IsOurProxy(byte[] data) { return Contains(data, "RRMF_PROXY_TAG"); }
    static bool IsOurRenderer(byte[] data) { return Contains(data, "RRHD_VERSION_TAG:"); }
    static void Add(List<Operation> operations, string path, byte[] after)
    {
        byte[] before = Read(path);
        if (!Same(before, after)) operations.Add(new Operation { Path = path, Before = before, After = after });
    }
    static List<Operation> Prepare(Game game, bool restore)
    {
        string exePath = Path.Combine(game.Root, ExeName);
        if (!File.Exists(exePath))
            throw new InvalidOperationException("RoadRash95.exe was not found in:\n" + game.Root + "\nChoose the game folder and try again.");
        var current = new byte[Managed.Length][];
        for (int i = 0; i < Managed.Length; ++i) current[i] = Read(Path.Combine(game.Root, Managed[i]));
        // An older install left a backup folder: its files were put there by this installer, and an original it saved is kept.
        bool legacy = Directory.Exists(game.LegacyFolder);
        var legacyOriginal = new byte[Managed.Length][];
        if (legacy) for (int i = 0; i < Managed.Length; ++i) legacyOriginal[i] = Read(Path.Combine(game.LegacyFolder, Managed[i]));
        var ours = new bool[Managed.Length];
        ours[0] = current[0] != null && (legacy || IsOurProxy(current[0]));
        ours[1] = current[1] != null && (legacy || IsOurRenderer(current[1]));
        var operations = new List<Operation>();
        if (restore)
        {
            for (int i = 0; i < Managed.Length; ++i)   // ddraw.dll first: once it is gone the game no longer references the renderer
            {
                string path = Path.Combine(game.Root, Managed[i]);
                if (current[i] != null && !ours[i])
                    throw new InvalidOperationException(Managed[i] + " has been replaced by a different file since the patch was installed. The current file was not overwritten.");
                byte[] saved = Read(game.BackupOf(i)) ?? legacyOriginal[i];
                if (current[i] != null) Add(operations, path, saved);
                if (Read(game.BackupOf(i)) != null && current[i] != null) Add(operations, game.BackupOf(i), null);
            }
            return operations;
        }
        var payload = new byte[Managed.Length][];
        for (int i = 0; i < Managed.Length; ++i) payload[i] = Resource(Managed[i]);
        for (int i = 0; i < Managed.Length; ++i)
        {
            // A file that is not ours (another wrapper) is somebody's earlier file: it is kept next to the new one.
            byte[] keep = (current[i] != null && !ours[i] && !Same(current[i], payload[i])) ? current[i] : legacyOriginal[i];
            if (keep == null) continue;
            byte[] existing = Read(game.BackupOf(i));
            if (existing != null && !Same(existing, keep))
                throw new IOException("A different " + Managed[i] + BackupSuffix + " already exists. It was not overwritten and no game files were changed.");
            if (existing == null) Add(operations, game.BackupOf(i), keep);
        }
        // The renderer is in place before ddraw.dll can reference it. Existing user settings are preserved.
        for (int i = Managed.Length - 1; i >= 0; --i) Add(operations, Path.Combine(game.Root, Managed[i]), payload[i]);
        string ini = Path.Combine(game.Root, IniName);
        if (!File.Exists(ini)) Add(operations, ini, Resource(IniName));
        // The pedestrian voice table is created once; an existing (possibly edited) one is left alone.
        string voices = Path.Combine(game.Root, "PedestrianVoices.ini");
        if (!File.Exists(voices)) Add(operations, voices, Resource("PedestrianVoices.ini"));
        return operations;
    }
    // The backup folder of earlier versions is not wanted any more.
    static void RemoveLegacyFolder(Game game)
    {
        try { if (Directory.Exists(game.LegacyFolder)) Directory.Delete(game.LegacyFolder, true); }
        catch (Exception) { }
    }
    static void SetFile(string path, string temporary, byte[] after)
    {
        if (after == null) File.Delete(path);
        else if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
    }
    static void Apply(List<Operation> operations)
    {
        var applied = new List<Operation>();
        try
        {
            foreach (var op in operations) if (op.After != null) op.Temporary = Stage(op.Path, op.After);
            foreach (var op in operations)
            {
                if (!Same(Read(op.Path), op.Before)) throw new IOException("A file changed during installation. Close the game and try again.");
                SetFile(op.Path, op.Temporary, op.After); applied.Add(op);
            }
            foreach (var op in operations) if (!Same(Read(op.Path), op.After)) throw new IOException("Verification after writing failed.");
        }
        catch (Exception error)
        {
            var failed = new List<string>();
            for (int i = applied.Count - 1; i >= 0; --i)
            {
                var op = applied[i]; string temporary = null;
                try
                {
                    if (op.Before != null) temporary = Stage(op.Path, op.Before);
                    SetFile(op.Path, temporary, op.Before);
                }
                catch { failed.Add(op.Path); }
                finally { Cleanup(temporary); }
            }
            if (failed.Count > 0) throw new IOException("The operation failed. Restore these files manually (the originals are the .multifix-backup files next to them):\n" + string.Join("\n", failed.ToArray()) + "\n" + error.Message);
            throw new IOException("The operation did not complete. Files replaced during this attempt were rolled back.\n" + error.Message, error);
        }
        finally { foreach (var op in operations) Cleanup(op.Temporary); }
    }
    static PatchResult Finish(Game game, string message, bool error)
    {
        string log = message + "\n\nLicense: PolyForm Noncommercial License 1.0.0\nhttps://polyformproject.org/licenses/noncommercial/1.0.0";
        try { if (game != null && Directory.Exists(game.Root)) File.WriteAllText(Path.Combine(game.Root, "RoadRash_MultiFix_Setup.log"), log + Environment.NewLine, Encoding.UTF8); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
        return new PatchResult(!error, message);
    }

    // Install or restore into the chosen folder. options (optional) maps "Section/Key" to its new value.
    public static PatchResult Execute(bool restore, string folder, Dictionary<string, bool> options)
    {
        Game game = null;
        try
        {
            if (string.IsNullOrEmpty(folder)) throw new InvalidOperationException("Choose the game folder first.");
            game = new Game(Path.GetFullPath(folder));
            foreach (var process in Process.GetProcessesByName("RoadRash95"))
            {
                process.Dispose(); throw new InvalidOperationException("Close Road Rash before running this patch.");
            }
            var operations = Prepare(game, restore);
            bool changed = operations.Count > 0;
            if (changed) Apply(operations);
            RemoveLegacyFolder(game);
            // Earlier versions called the renderer RoadRashHD.dll; the proxy no longer loads it.
            try { string legacy = Path.Combine(game.Root, "RoadRashHD.dll"); if (File.Exists(legacy)) { byte[] old = File.ReadAllBytes(legacy); if (IsOurRenderer(old) || Contains(old, "RoadRashHD")) File.Delete(legacy); } } catch (Exception) { }
            if (!restore) { MergeMissingIniKeys(game); WriteOptions(game, options ?? new Dictionary<string, bool>()); }
            if (!changed)
                return Finish(game, restore ? "The game is already in its pre-install state." : "This version of MultiFix is already installed. Settings were updated.", false);
            return Finish(game, restore ? "ddraw.dll and RoadRashMultiFix.dll were removed (any earlier files were restored). RoadRashHD.ini was kept." :
                "RoadRash30-MultiFix installed.\nRoadRash95.exe was not changed by this install (only the optional 4 GB switch touches it).\nLaunch the game as usual (GOG Galaxy, EA app, Steam or the desktop shortcut).\nYour saves are unchanged. Settings are in RoadRashHD.ini; if something looks wrong, check RoadRashHD.log in the game folder.", false);
        }
        catch (Exception error)
        {
            string message = error.Message;
            if (error is UnauthorizedAccessException || error.InnerException is UnauthorizedAccessException)
                message += "\nThe game folder is not writable. Run this patch as administrator (right-click, Run as administrator).";
            return Finish(game, message, true);
        }
    }
    // An older RoadRashHD.ini lacks the keys added later. They are added with the same explanatory comments as in the
    // ini that ships with this version (a new ini is created from it), in the right section; nothing existing changes.
    static void MergeMissingIniKeys(Game game)
    {
        try
        {
            string path = Path.Combine(game.Root, IniName);
            if (!File.Exists(path)) return;
            byte[] raw = File.ReadAllBytes(path);
            if (raw.Length >= 2 && ((raw[0] == 0xFF && raw[1] == 0xFE) || (raw[0] == 0xFE && raw[1] == 0xFF))) return;   // UTF-16: leave it alone
            Encoding encoding = Encoding.Default;
            bool crlf = Encoding.ASCII.GetString(raw).Contains("\r\n");
            var lines = new List<string>(encoding.GetString(raw).Replace("\r\n", "\n").Split('\n'));
            if (lines.Count > 0 && lines[lines.Count - 1] == "") lines.RemoveAt(lines.Count - 1);
            // the shipped ini: each key with the comment lines directly above it
            string shipped = Encoding.UTF8.GetString(Resource(IniName)).Replace("\r\n", "\n");
            string section = "";
            var comments = new List<string>();
            bool changed = false;
            foreach (string line in shipped.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0) { comments.Clear(); continue; }
                if (trimmed.StartsWith(";")) { comments.Add(line); continue; }
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]")) { section = trimmed.Substring(1, trimmed.Length - 2); comments.Clear(); continue; }
                int equals = trimmed.IndexOf('=');
                if (equals <= 0 || section.Length == 0) { comments.Clear(); continue; }
                string key = trimmed.Substring(0, equals).Trim();
                // find the section and the key in the existing file
                int sectionStart = -1, sectionEnd = lines.Count; bool found = false;
                for (int i = 0; i < lines.Count; ++i)
                {
                    string current = lines[i].Trim();
                    if (current.StartsWith("[") && current.EndsWith("]"))
                    {
                        if (sectionStart >= 0) { sectionEnd = i; break; }
                        if (string.Equals(current.Substring(1, current.Length - 2).Trim(), section, StringComparison.OrdinalIgnoreCase)) sectionStart = i;
                    }
                    else if (sectionStart >= 0 && !current.StartsWith(";") && current.IndexOf('=') > 0 &&
                             string.Equals(current.Substring(0, current.IndexOf('=')).Trim(), key, StringComparison.OrdinalIgnoreCase)) found = true;
                }
                if (!found)
                {
                    var add = new List<string>(comments); add.Add(line);
                    if (sectionStart < 0) { lines.Add(""); lines.Add("[" + section + "]"); lines.AddRange(add); }
                    else
                    {
                        int at = sectionEnd;   // after the last non-blank line of the section
                        while (at > sectionStart + 1 && lines[at - 1].Trim().Length == 0) --at;
                        lines.InsertRange(at, add);
                    }
                    changed = true;
                }
                comments.Clear();
            }
            if (changed) File.WriteAllText(path, string.Join(crlf ? "\r\n" : "\n", lines.ToArray()) + (crlf ? "\r\n" : "\n"), encoding);
        }
        catch (Exception) { }   // the options are still written key by key
    }
    static void WriteOptions(Game game, Dictionary<string, bool> options)
    {
        string ini = Path.Combine(game.Root, IniName);
        foreach (var o in Options)
        {
            bool value;
            if (options.TryGetValue(o.Section + "/" + o.Key, out value))
                WritePrivateProfileString(o.Section, o.Key, (value != o.Inverted) ? "1" : "0", ini);
            else
            {
                // No explicit choice (quiet install): add keys missing from an older ini with their defaults.
                var current = new StringBuilder(8);
                GetPrivateProfileString(o.Section, o.Key, "?", current, 8, ini);
                if (current.ToString() == "?") WritePrivateProfileString(o.Section, o.Key, (o.Default != o.Inverted) ? "1" : "0", ini);
            }
        }
    }

    // ------------------------------------------------------------------ entry point

    static Form CreateWindow() { return new PatchWindow(); }

    [STAThread]
    static int Main(string[] args)
    {
        bool quiet = false, restore = false; string folder = null;
        foreach (string arg in args)
        {
            if (string.Equals(arg, "/quiet", StringComparison.OrdinalIgnoreCase)) quiet = true;
            else if (string.Equals(arg, "/restore", StringComparison.OrdinalIgnoreCase)) restore = true;
            else if (arg.StartsWith("/path:", StringComparison.OrdinalIgnoreCase)) folder = arg.Substring(6).Trim('"');
            else
            {
                var invalid = Finish(null, "Usage: double-click to open the installer; /path:\"C:\\Games\\Road Rash\" selects the game folder; /restore restores directly; /quiet runs silently.", true);
                if (!quiet) MessageBox.Show(invalid.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
        if (quiet || restore)
        {
            if (folder == null)
            {
                var found = Detect();
                if (found.Count == 0)
                {
                    var none = Finish(null, "RoadRash95.exe was not found. Place this EXE in the game folder or pass /path:\"<game folder>\".", true);
                    if (!quiet) MessageBox.Show(none.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                if (found.Count > 1 && found[0].Source != "This folder")
                {
                    var many = Finish(null, "More than one Road Rash installation was found. Pass /path:\"<game folder>\" to choose one.", true);
                    if (!quiet) MessageBox.Show(many.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                folder = found[0].Path;
            }
            var result = Execute(restore, folder, null);
            if (!quiet) MessageBox.Show(result.Message, Title, MessageBoxButtons.OK,
                                        result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            return result.Success ? 0 : 1;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var window = new PatchWindow(folder)) Application.Run(window);
        return 0;
    }
}
