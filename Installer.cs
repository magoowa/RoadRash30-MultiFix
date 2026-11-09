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
[assembly: AssemblyDescription("Native HD, aspect ratio support, texture filtering and fixes for the GOG, EA app and Steam English builds. Loads through ddraw.dll; RoadRash95.exe is never modified.")]
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
    public string Section, Key, Label, Hint;
    public bool Default;
    public PatchOption(string section, string key, bool def, string label, string hint)
    { Section = section; Key = key; Default = def; Label = label; Hint = hint; }
}

internal static class Installer
{
    public const string Title = ModInfo.Name + " " + ModInfo.Version;
    const string ExeName = "RoadRash95.exe", IniName = "RoadRashHD.ini";
    const string BackupFolder = "ResolutionPatch_Backup";
    // Files this installer owns. Both are embedded in the installer.
    static readonly string[] Managed = { "ddraw.dll", "RoadRashMultiFix.dll" };
    const string Magic = "RRHD_BACKUP_V2";

    // Executables the patch was verified with. Anything else is still allowed (the
    // renderer checks the game code itself and does nothing on a mismatch).
    static readonly Dictionary<string, string> KnownExecutables = new Dictionary<string, string>
    {
        { "9e40e25737149ab095061b7154797a35495faea97644fafc5a1e616f1400c968",
          "English build, identical in GOG (60184028392664413) and Steam (26105967)" }
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
        new PatchOption("Window", "WideWindow", true, "Widescreen (16:9) Game", "Plays the whole game in widescreen (wide field of view) instead of the original 4:3"),
        new PatchOption("Window", "AltEnterFullscreen", true, "Alt+Enter / F11 Fullscreen Toggle", "Switches between the window and borderless fullscreen"),
        new PatchOption("Input", "SymmetricSteering", true, "Symmetric Analog Steering", "Controller stick turns right as gently as left (the game made right about twice as sensitive)"),
        new PatchOption("Input", "PadDialogs", true, "Controller Answers Dialogs", "A = OK / Yes and B = Cancel / No in the game's message boxes (for example when quitting)"),
        new PatchOption("Window", "AutoHideCursor", true, "Auto-Hide Mouse Cursor", "Hidden at start and after 3 s without mouse movement; returns when you move the mouse"),
        new PatchOption("Window", "StartFullscreen", true, "Start in Fullscreen", "Opens the game in borderless fullscreen"),
        new PatchOption("Game", "SkipIntro", false, "Skip Intro Videos", "Skips the studio logos and the intro movie (menu music still starts)"),
    };

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern uint GetPrivateProfileInt(string section, string key, int def, string file);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern uint GetPrivateProfileString(string section, string key, string def, StringBuilder buffer, uint size, string file);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool WritePrivateProfileString(string section, string key, string value, string file);

    sealed class Game
    {
        public string Root, Backup, StatePath;
        public Game(string root)
        {
            Root = root; Backup = Path.Combine(root, BackupFolder); StatePath = Path.Combine(Backup, "state.txt");
        }
    }
    sealed class State
    {
        // Hash of each managed file before installation ("-" = absent) and the
        // hash this installer wrote.
        public string[] Before = new string[Managed.Length], Installed = new string[Managed.Length];
        public byte[] Bytes()
        {
            var lines = new List<string> { Magic };
            lines.AddRange(Before); lines.AddRange(Installed);
            return Encoding.UTF8.GetBytes(string.Join("\n", lines.ToArray()) + "\n");
        }
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
        if (hash != null && KnownExecutables.TryGetValue(hash, out known)) sb.Append("Executable: ").Append(known).Append(" (verified)\r\n");
        else sb.Append("Executable: not on the verified list (SHA-256 ").Append(hash == null ? "unreadable" : hash.Substring(0, 12) + "...")
               .Append(fileVersion != "" ? ", file version " + fileVersion : "").Append("). The patch checks the game code at start-up and stays inactive if it does not match.\r\n");
        sb.Append("MultiFix: ").Append(Status(folder).Describe());
        return sb.ToString();
    }
    // What is in the game folder: our ddraw.dll proxy and the renderer DLL, and the renderer's version.
    public sealed class PatchStatus
    {
        public bool ProxyOurs, ProxyPresent, RendererPresent, RendererCurrent;
        public string RendererVersion = "";
        public bool Installed { get { return ProxyOurs && RendererPresent; } }
        public bool Outdated { get { return Installed && !RendererCurrent; } }
        public string Describe()
        {
            if (Installed)
                return RendererCurrent ? "installed, DLL version " + RendererVersion + " (up to date)"
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
            s.ProxyOurs = s.ProxyPresent && HashFile(proxy) == Hash(Resource("ddraw.dll"));
            s.RendererPresent = File.Exists(renderer);
            if (s.RendererPresent)
            {
                s.RendererCurrent = HashFile(renderer) == Hash(Resource("RoadRashMultiFix.dll"));
                s.RendererVersion = TagVersion(File.ReadAllBytes(renderer));
                if (s.RendererCurrent && s.RendererVersion == "") s.RendererVersion = ModInfo.Version;
            }
        }
        catch (Exception) { }
        return s;
    }
    // The renderer DLL carries "RRHD_VERSION_TAG:<version>" followed by a NUL byte.
    static string TagVersion(byte[] data)
    {
        byte[] tag = Encoding.ASCII.GetBytes("RRHD_VERSION_TAG:");
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
    public static bool SaveLanguage(string folder, string name)
    {
        try
        {
            string ini = Path.Combine(folder ?? "", IniName);
            return File.Exists(ini) && WritePrivateProfileString("Game", "Language", name, ini);
        }
        catch (Exception) { return false; }
    }
    // Writes one option to RoadRashHD.ini immediately. Returns false when the ini does not exist yet.
    public static bool SaveOption(string folder, PatchOption option, bool value)
    {
        try
        {
            string ini = Path.Combine(folder ?? "", IniName);
            if (!File.Exists(ini)) return false;
            return WritePrivateProfileString(option.Section, option.Key, value ? "1" : "0", ini);
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
        return GetPrivateProfileInt(option.Section, option.Key, option.Default ? 1 : 0, ini) != 0;
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
    static void Preserve(string path, byte[] bytes)
    {
        if (bytes == null) return;
        if (File.Exists(path))
        {
            if (!Same(File.ReadAllBytes(path), bytes)) throw new IOException("An existing backup does not match the current file; the backup was not overwritten: " + path);
            return;
        }
        string temporary = Stage(path, bytes);
        try { File.Move(temporary, path); } finally { Cleanup(temporary); }
    }
    static State ReadState(Game game)
    {
        if (!File.Exists(game.StatePath)) return null;
        string[] lines = File.ReadAllLines(game.StatePath, Encoding.UTF8);
        if (lines.Length != 1 + 2 * Managed.Length || lines[0] != Magic) throw new InvalidDataException("The backup record is corrupt.");
        var state = new State();
        for (int i = 0; i < Managed.Length; ++i)
        {
            state.Before[i] = lines[1 + i]; state.Installed[i] = lines[1 + Managed.Length + i];
            if (Hash(Read(Path.Combine(game.Backup, Managed[i]))) != state.Before[i])
                throw new InvalidDataException("Backup verification failed. No game files were changed.");
        }
        return state;
    }
    static void CheckCurrent(State state, byte[][] current)
    {
        for (int i = 0; i < Managed.Length; ++i)
        {
            string hash = Hash(current[i]);
            if (hash != state.Before[i] && hash != state.Installed[i])
                throw new InvalidOperationException(Managed[i] + " has been replaced by a different file since the patch was installed. The current file was not overwritten.");
        }
    }
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
        State state = ReadState(game);
        var operations = new List<Operation>();
        if (restore)
        {
            if (state == null) throw new InvalidOperationException("There is no backup made by this installer. No game files were changed.");
            CheckCurrent(state, current);
            // ddraw.dll first: once it is gone the game no longer references the renderer.
            for (int i = 0; i < Managed.Length; ++i)
                Add(operations, Path.Combine(game.Root, Managed[i]), Read(Path.Combine(game.Backup, Managed[i])));
            return operations;
        }
        var payload = new byte[Managed.Length][];
        for (int i = 0; i < Managed.Length; ++i) payload[i] = Resource(Managed[i]);
        if (state == null)
        {
            Directory.CreateDirectory(game.Backup);
            state = new State();
            for (int i = 0; i < Managed.Length; ++i)
            {
                string backup = Path.Combine(game.Backup, Managed[i]);
                // A file that already equals ours needs no backup. Anything else is
                // somebody's earlier file and is kept so Restore can bring it back.
                byte[] before = Same(current[i], payload[i]) ? null : current[i];
                // Reuse only identical leftovers from an interrupted attempt; never
                // overwrite someone else's earlier backup.
                if (before == null && File.Exists(backup))
                    throw new IOException("The backup folder contains a different " + Managed[i] + ". The backup was not overwritten.");
                Preserve(backup, before);
                state.Before[i] = Hash(before);
            }
        }
        else CheckCurrent(state, current);
        for (int i = 0; i < Managed.Length; ++i) state.Installed[i] = Hash(payload[i]);
        // The renderer is in place before ddraw.dll can reference it. Existing user
        // settings are preserved, including during reinstallation.
        for (int i = Managed.Length - 1; i >= 0; --i) Add(operations, Path.Combine(game.Root, Managed[i]), payload[i]);
        string ini = Path.Combine(game.Root, IniName);
        if (!File.Exists(ini)) Add(operations, ini, Resource(IniName));
        Add(operations, game.StatePath, state.Bytes());
        return operations;
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
            if (failed.Count > 0) throw new IOException("The operation failed. Restore these files manually from the backup folder:\n" + string.Join("\n", failed.ToArray()) + "\n" + error.Message);
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
            // Earlier versions called the renderer RoadRashHD.dll; the proxy no longer loads it.
            try { string legacy = Path.Combine(game.Root, "RoadRashHD.dll"); if (File.Exists(legacy)) File.Delete(legacy); } catch (Exception) { }
            if (!restore) WriteOptions(game, options ?? new Dictionary<string, bool>());
            if (!changed)
                return Finish(game, restore ? "The game is already in its pre-install state." : "This version of MultiFix is already installed. Settings were updated.", false);
            return Finish(game, restore ? "ddraw.dll and RoadRashMultiFix.dll were removed (any earlier files were restored). RoadRashHD.ini was kept." :
                "RoadRash30-MultiFix installed.\nRoadRash95.exe was not modified.\nLaunch the game as usual (GOG Galaxy, EA app, Steam or the desktop shortcut).\nYour saves are unchanged. Settings are in RoadRashHD.ini; if something looks wrong, check RoadRashHD.log in the game folder.", false);
        }
        catch (Exception error)
        {
            string message = error.Message;
            if (error is UnauthorizedAccessException || error.InnerException is UnauthorizedAccessException)
                message += "\nThe game folder is not writable. Run this patch as administrator (right-click, Run as administrator).";
            return Finish(game, message, true);
        }
    }
    static void WriteOptions(Game game, Dictionary<string, bool> options)
    {
        string ini = Path.Combine(game.Root, IniName);
        foreach (var o in Options)
        {
            bool value;
            if (options.TryGetValue(o.Section + "/" + o.Key, out value))
                WritePrivateProfileString(o.Section, o.Key, value ? "1" : "0", ini);
            else
            {
                // No explicit choice (quiet install): add keys missing from an older ini with their defaults.
                var current = new StringBuilder(8);
                GetPrivateProfileString(o.Section, o.Key, "?", current, 8, ini);
                if (current.ToString() == "?") WritePrivateProfileString(o.Section, o.Key, o.Default ? "1" : "0", ini);
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
