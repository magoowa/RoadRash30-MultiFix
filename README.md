<p align="center"><img src="docs/logo.png" width="420" alt="Road Rash 30 MultiFix"></p>

<p align="center"><b>v2.18.0</b> &nbsp;|&nbsp; <a href="https://github.com/magoowa/RoadRash30-MultiFix/releases/latest"><b>Download the latest release</b></a> &nbsp;|&nbsp; <a href="CHANGELOG.md">Changelog</a></p>

Fixes, widescreen and a Direct3D 11 renderer for **Road Rash: 30th Anniversary Edition** (GOG, EA app and Steam). Authors: **OBLPlife** (original author) and **magoowa**.

A GOG-compatible rework of [OBLPlife/RoadRash30th-HD](https://github.com/OBLPlife/RoadRash30th-HD) for the GOG, EA app and Steam editions.

**`RoadRash95.exe` is never modified.** The patch is a `ddraw.dll` proxy that the game loads from its own folder. It forwards all DirectDraw calls to the real system `ddraw.dll` and loads `RoadRashMultiFix.dll` at startup. The renderer checks the game code before hooking and does nothing on an executable it does not recognize (see `RoadRashHD.log`). Verified with the GOG build `60184028392664413` (SHA-256 `9e40e25737149ab095061b7154797a35495faea97644fafc5a1e616f1400c968`), which is byte-identical to the Steam build 26105967.

![The MultiFix launcher](docs/launcher.png)

## Install and Restore

1. Download `RoadRash30-MultiFix_v<version>.zip` from the [latest release](https://github.com/magoowa/RoadRash30-MultiFix/releases/latest) and unpack `RoadRash_MultiFix_Setup_v<version>.exe` (or build it yourself, see below). You can place it in the game folder, next to `RoadRash95.exe`, or run it from anywhere and pick the folder.
2. Double-click the EXE and select **Install**. If the game is in `C:\Program Files` or `C:\Program Files (x86)`, right-click the EXE and choose **Run as administrator**.
3. Launch the game as usual (GOG Galaxy or the desktop shortcut). Resolution is detected automatically; adjust it under **Graphics and Sound → Race Resolution** if desired.

Install copies `ddraw.dll`, `RoadRashMultiFix.dll` and (if missing) `RoadRashHD.ini` and `PedestrianVoices.ini` into the game folder. If a different `ddraw.dll` (another wrapper) is already there, it is kept next to the new one as `ddraw.dll.multifix-backup` and put back on **Restore**; no backup folder is created (an old `ResolutionPatch_Backup` folder from earlier versions is migrated and removed). To restore, open the same EXE and select **Restore**, or run it with `/restore`. `/quiet` suppresses dialogs. You can also remove the patch by simply deleting `ddraw.dll` and `RoadRashMultiFix.dll`.

### GOG notes

- GOG Galaxy's **Verify / Repair** will not undo the patch, because the original files are untouched. Galaxy may remove the extra files on a full reinstall; just run the installer again.
- Another `ddraw.dll` wrapper (dgVoodoo, cnc-ddraw, DDrawCompat, ...) cannot be used at the same time, because both use the same file name.

## Linux, Steam Deck and Proton (experimental)

The patch is a `ddraw.dll` proxy, so it can be loaded under Wine/Proton with a DLL override. **This is untested**; reports are welcome (please attach `RoadRashHD.log`).

**The launch option in step 3 is required.** Dropping the files into the game folder is not enough: Proton only uses our `ddraw.dll` when told to, so without the launch option the game starts without MultiFix (and no `RoadRashHD.log` appears).

1. Download the release zip and unpack it. The `linux/` folder holds `install.sh`, `ddraw.dll`, `RoadRashMultiFix.dll` and `RoadRashHD.ini`.
2. Put the files into the game folder, the one that contains `RoadRash95.exe`. In Steam (Desktop mode): right-click the game, **Manage > Browse local files**. Either
   - copy `ddraw.dll`, `RoadRashMultiFix.dll` and `RoadRashHD.ini` there by hand, or
   - run the script from a terminal opened in the `linux` folder: `bash install.sh` (it finds the Steam copy, including one on the SD card, by itself) or `bash install.sh "/path/to/Road Rash 30th Anniversary Edition"`. Running it with `bash` needs no execute permission. `--remove` undoes it.
3. In Steam open the game's **Properties > General > Launch Options** and paste **`WINEDLLOVERRIDES="ddraw=n,b" %command%`** (outside Steam: `WINEDLLOVERRIDES="ddraw=n,b" wine RoadRash95.exe`; Heroic and Lutris: add `WINEDLLOVERRIDES` = `ddraw=n,b` as an environment variable of the game).
4. Start the game. `RoadRashHD.log` appears in the game folder; if it does not, the override was not applied.

Notes: the Direct3D 11 renderer uses DXVK under Proton. Its shaders are compiled at build time, so no shader compiler is needed at run time. Under Wine the Direct3D surface is created on the game window itself (`[Graphics] ChildSurface=1` forces the Windows arrangement). If the picture is wrong, set `D3D11=0` in `RoadRashHD.ini` for the CPU renderer. `RoadRashHD.log` says "running under Wine ..." when Wine is detected.

## Features

See the [changelog](CHANGELOG.md) for what changed in each version.

Inherited from the original project: automatic native resolution, native HD re-rasterization of roads and objects, aspect-ratio adaptation (standard / widescreen / ultrawide), in-game resolution selection, menu and rendering improvements.

Added in this fork:

- **No EXE modification:** loads through a `ddraw.dll` proxy; GOG Galaxy "Verify / Repair" does not undo it.
- **Installer and launcher:** detects GOG, EA app and Steam installations, shows the executable version, has one row per option (read from and saved to `RoadRashHD.ini` live), a **Play** button, detection of the MultiFix installation and its DLL version in the game folder (if it is missing or outdated, Play asks whether to install/update first; Yes or No, the game starts either way), backup/restore, HiDPI-safe layout and a custom icon.
- **Pedestrian sounds:** new voices and collision reactions for every kind of pedestrian (including the flag girls and the people at the start), and pedestrians that are no longer far quieter than the rider.
- **Sprite fixes:** the damaged shadow of the first punch frame and the buildings that hover above the ground (red barn, brown shop, white house) are fixed in memory.
- **Controllers:** analog throttle on the right trigger; a native DualSense no longer scrolls the menus by itself; after Alt+Tab or a click on another monitor, Start + Back on an Xbox pad brings the game back to the front (always on); optional DualSense support (Bluetooth, d-pad), based on the findings and the local patch of **CypherCripto** ([issue #4](https://github.com/magoowa/RoadRash30-MultiFix/issues/4)).
- **Dual monitor UX improvements (optional):** the mouse is not confined to the game window and the game is not minimized when it loses focus.
- **Stutter Fix:** the controller scan that froze the game for ~165 ms every two seconds now runs in the background.
- **Windowed mode from the game menu** keeps the configured window size (`[Window] Width/Height` or automatic 16:9) instead of switching to 4:3.
- **Netplay download:** the launcher shows whether `wsock32.dll` (OKPlay) is in the game folder. If not, the **download netplay** link fetches the newest package from oldkid.net (OKPlay GitHub release as fallback) and unpacks `netcheck.exe`, `okplay.ini` (an existing one is kept), `wsock32.dll` and `oldkid_netplay_readme.txt` into the game folder.
- **FMV Volume:** a slider in the launcher sets the volume of all intro and story videos (.avi and .mp4), 0-100 %.
- **Fix Bike Colors:** the Rat and Super bikes have each other's colours in the race (the shop pictures are right); the two groups of five colour tables are swapped back (the same change as Anakin94's fix for the 1996 exe). Thanks to Anakin94 and peemosa for finding the bug.
- **Fix Text Encoding:** Unicode text the game misreads (the trademark sign shown as `â„¢`, curly quotes, accents) is displayed correctly even with Windows' UTF-8 system code page.
- **Widescreen game, Alt+Enter / F11:** the whole game runs in widescreen (16:9 window, wide field of view) and a borderless fullscreen toggle is added.
- **Cleaner textures in Direct3D:** thin far-away strips (water, road) and squeezed textures are averaged instead of aliasing into streaks; sprites are never filtered.
- **Symmetric Analog Steering:** with an analog stick (Xbox controllers and similar) the right turn was about twice as sensitive as the left one, started with a jump and overshot by 25 %. The left and right mappings now match.
- **Auto-hide mouse cursor:** the cursor is hidden from the start and after 3 seconds without mouse movement, and comes back when you move the mouse.
- **Language selection:** the launcher lists the languages found in the game's TEXT folder (English, French, German, Italian, Spanish) and saves the choice to `RoadRashHD.ini`, no administrator rights or registry editing needed.
- **Controller answers dialogs:** the game's message boxes (for example the quit confirmation) can be answered with the pad: A = OK / Yes, B = Cancel / No; the D-pad moves between buttons.
- **Controller skips videos and operates the Race Resolution dialog:** A, B, X, Y, Start or Back skip a video; in Race Resolution the D-pad picks the resolution, A applies and B cancels.
- **Sprite Position Interpolation:** far riders, cars and pedestrians are updated every frame instead of every 2-8 frames (`FullRateObjects`).
- **Direct3D 11 hardware renderer** (`D3D11=1`, default): the scene, textures, palette and presentation run on the GPU, with the CPU/GDI path as an automatic fallback. This makes **bilinear texture filtering** (`TextureFilter`, default on) practically free even at 4K.
- **Menus and text are crisp by default**; texture filtering (`TextureFilter`) applies only to the 3D game environment and only in the Direct3D renderer. Bilinear filtering of the text and the interface is available as an option (`SmoothText`, see Configuration). **Skip intro** (`SkipIntro`, off by default) keeps the menu music.

### v1.1.1 Animation Crash Fix

A missing-animation-metadata case caused crashes on the Peninsula track. **The original game's animation-frame selector also crashes with the same invalid input.** The update checks the animation resource type, skips invalid frames and adds regression tests.

## Configuration (Optional)

All options are in `RoadRashHD.ini` (game folder) and in the installer/launcher window; changes apply the next time the game starts. `1` = on, `0` = off.

| Section | Option | Default | What it does |
| --- | --- | :---: | --- |
| `[Graphics]` | `D3D11` | 1 | Direct3D 11 hardware renderer. Falls back to the CPU renderer automatically if it cannot start; `0` forces the CPU renderer. |
| `[Graphics]` | `TextureFilter` | 1 | Smooth (bilinear) textures of the 3D game environment in races (road, buildings, sky). Direct3D renderer only; menus and text are not touched by this option (see `SmoothText`). |
| `[Graphics]` | `SmoothText` | 0 | Bilinear filtering of the text and the interface instead of crisp pixels: the menus (the 640x480 menu picture is sampled bilinearly into the window) and the race HUD and text layer (Direct3D renderer). Ini only, not shown in the launcher. |
| `[Graphics]` | `Quality` | High | Resolution preset for weaker graphics: High = window size, Medium = 720p, Low = 540p, Minimum = 480p, Auto = starts at window size and, when races run slower than 12.5 ms per frame twice in a row (about 10 s each), lowers the height for the next start (`AutoRenderHeight`). A fixed `RenderHeight` overrides it. Ini only. |
| `[Performance]` | `HighPerformance` | 1 | Turns off Windows power throttling (EcoQoS) for the game, requests a 1 ms timer and raises the priority slightly (above normal). Helps laptops and integrated GPUs. |
| `[Performance]` | `LowLatency` | 0 | Keep it at 0: performance boost on battery-powered Intel iGPU. With 1 (waitable swap chain with one queued frame and a tearing present) an Intel Iris Xe laptop on battery dropped from 60 to 45 fps. Launcher: **iGPU Performance Boost** (on = 0). |
| `[Graphics]` | `VSync` | 0 | Waits for the monitor refresh before showing each frame: no tearing, slightly more latency. |
| `[Debug]` | `QuadDiagnostics` | 0 | Troubleshooting for picture glitches: quads with extreme corners are written to `RoadRashHD.log` and the frame is saved as `RoadRashHD_glitch_N.bmp` (at most 8). Direct3D renderer only. |
| `[Window]` | `WideWindow` | 1 | Widescreen game: the whole game fills the window or screen with a wide field of view, whatever its shape. 0 = the original 4:3 picture (a 4:3 window; in fullscreen or a wider window it is centred with black bars, no extended backgrounds). |
| `[Window]` | `AltEnterFullscreen` | 1 | Alt+Enter and F11 switch between window and borderless fullscreen. |
| `[Window]` | `DualMonitorUX` | 0 | Dual monitor UX improvements: the mouse cursor is not confined to the game window and the game is not minimized when it loses focus (the music still stops). |
| `[Window]` | `AutoHideCursor` | 1 | Hides the mouse cursor at start and after 3 s without mouse movement; it returns when the mouse moves. |
| `[Window]` | `StartFullscreen` | 1 | Starts in fullscreen instead of a window. |
| `[Text]` | `FixEncoding` | 1 | Fixes garbled special characters (for example `Rashâ„¢` instead of the trademark sign, or `â€œEnderâ€` instead of curly quotes). The game reads its strings with `LoadStringA`, which returns UTF-8 when Windows uses the "Use Unicode UTF-8 for worldwide language support" setting, while the game's bitmap font expects Windows-1252. |
| `[Input]` | `KeyboardAndPad` | 1 | Keyboard and controller work at the same time, whatever control device the loaded profile has selected (the original game reads only the selected one); missing pad assignments of a keyboard profile are filled in with the game's defaults. Launcher: **Keyboard + Controller Together**. |
| `[Input]` | `AsyncControllerScan` | 1 | Stutter fix: searches for controllers in a background thread. On the game thread it opened every HID device every two seconds, and one slow device froze the game for ~165 ms (credits, menus, races). |
| `[Input]` | `DualSense` | 0 | Experimental, untested without the pad (issue #4): a DualSense or DualShock 4 over HID: Bluetooth reports are understood and the d-pad steers and chooses in the menus (diagonals too). The fix for a native DualSense that scrolled the menus upward by itself is always on. |
| `[Input]` | `SymmetricSteering` | 1 | Analog stick steering: right turn is as gentle as left (the original mapping made right about twice as sensitive). |
| `[Game]` | `Language` | Auto | Game text language: `Auto` (the game's own setting), `English`, `French`, `German`, `Italian` or `Spanish`. Needs the matching DLL in the game's TEXT folder. |
| `[Input]` | `PadDialogs` | 1 | The controller answers the game's message boxes (A = OK / Yes, B = Cancel / No), for example the quit confirmation. Skipping videos and the Race Resolution dialog work with the controller regardless of this option. |
| `[Input]` | `AnalogThrottle` / `ThrottleCurve` / `ThrottleMinimum` | 1 / 1.8 / 2 | The right trigger of an Xbox pad gives a throttle that follows its pressure (the game treats it as a button: full throttle above 12%). Curve 1 = linear, larger = gentler at the start. |
| `[Game]` | `FullRateObjects` | 1 | Sprite position interpolation: far riders, cars and pedestrians are updated every frame instead of every 2-8 frames. |
| `[Game]` | `FixPostcards` | 1 | Repairs the broken left strip of the GAME LOADING picture. |
| `[Game]` | `SpriteFixes` | 1 | Sprite fixes: the shadow of the first frame of the punch (drawn far from the bike in the original game) and the buildings that hover above the ground (red barn, brown wooden shop with the sign, white house), which are lowered. Tuned with `LowerRedBarn`, `LowerBrownShop`, `LowerWhiteHouse`. |
| `[Game]` | `LowerRedBarn` / `LowerBrownShop` / `LowerWhiteHouse` | 2 / 3 / 10 | How far the red barn, the brown wooden shop with the sign and the white house are lowered, in rows of their full-size picture (decimals allowed). |
| `[Game]` | `FixBikeColors` | 1 | Gives the Rat and Super bikes their correct colours in the race. |
| `[Game]` | `SkipIntro` | 0 | Skips the studio logos and the intro movie; the menu music still plays. |
| `[Game]` | `RememberLastSave` | 1 | The Load Game dialog opens with the save used last already selected (`LastSave`, written by the game). The controller works the Open / Save as dialogs: D-pad up / down choose, A opens, B cancels (`[Input] PadDialogs`). |
| `[Audio]` | `PedestrianVoiceRules` | 1 | Each kind of pedestrian shouts with its own pool of voices (table: `PedestrianVoices.ini`, editable); flag girls and the people at the start react to a collision too. |
| `[Audio]` | `LouderPedestrians` | 1 | The voices of people other than the rider (bystanders, bikers, tourists, skaters, cyclists, cops) and the footsteps are made louder. The game plays them at a fixed -17.6 dB. A louder copy is made once in `%LOCALAPPDATA%\RoadRashMultiFix\audio` from the game's own files, which are never changed. |
| `[Audio]` | `PedestrianGainDb` | 3 | How much louder the sounds themselves are, in dB (0-12); the footsteps get twice as much again. |
| `[Audio]` | `PedestrianVolumeRelief` | 60 | Reduces the attenuation the game applies to those sounds, in percent (0 = as the game sets it, 100 = none). |
| `[Audio]` | `PedestrianVoiceRelief` | 100 | The same for the pedestrians' voices alone (not the thump or the footsteps); 100 = as loud as the rider's own sounds. |
| `[Audio]` | `FemaleVoiceFallback` | 1 | The woman pedestrian that has no voice in the game's data gets the unused voices `FG1_WATC` / `FG2_EEK`. |
| `[Audio]` | `PedestrianVoiceGapMs` | 120 | Minimum time in milliseconds between two first-reaction voices of pedestrians (0 = no limit). |
| `[Game]` | `VideoVolume` | 100 | Volume of all videos (.avi and .mp4) in percent of the game's volume (launcher: FMV Volume slider). |

Advanced options, not shown in the launcher (add them to the ini):

- `[Graphics] RenderHeight=1080`: draws the 3D scene at a fixed height (here 1080 lines) and scales it to the window. Default `0` matches the window size, which is what you want in almost every case.
- `[Debug] Profile=1`: writes frame-time statistics to `RoadRashHD.log`, for troubleshooting stutter.

## Build from Source

Requires Windows, Python 3.10+, Visual Studio C++ Desktop Development tools, Windows SDK and a .NET Framework 4.x compiler.
```powershell
python build.py
```

Build and run tests:

```powershell
python build.py --tests
python tests/test_installer.py --original-dir "C:\GOG Games\Road Rash - 30th Anniversary Edition"
```

Rendering code is in `src/`; the ddraw proxy is `src/ddraw_proxy.cpp`; installer code is in `Installer.cs` and `PatchWindow.cs`; tests are in `tests/`.

## Credits

Original project and renderer by **OBLPlife** (https://github.com/OBLPlife/RoadRash30th-HD). Fork by **magoowa**: GOG/EA/Steam support, the ddraw.dll loader, the Direct3D 11 renderer, the installer/launcher and the fixes above.

- **CypherCripto**: DualSense support (HID filter, Bluetooth reports, d-pad steering) and the cursor / focus findings ([issue #4](https://github.com/magoowa/RoadRash30-MultiFix/issues/4)).
- **Oldkid.net**: netplay.
- **Anakin94**: the inspiration for the new pedestrian sounds and the report of the punch shadow bug.
- **jmb20** and **ChilliChickenDry**: Wine, Proton and CrossOver troubleshooting.
- **peemosa** and **neparij**: contributions.

## License

This project uses the [PolyForm Noncommercial License 1.0.0](LICENSE). Noncommercial use, modification and redistribution are permitted; **unauthorized commercial use and resale are prohibited**.
