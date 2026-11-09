# RoadRash30-MultiFix

Fixes, widescreen and a Direct3D 11 renderer for **Road Rash: 30th Anniversary Edition** (GOG, EA app and Steam, English). Mod version 2.13.7. Authors: **OBLPlife** (original author) and **magoowa**.

For the **GOG** and Steam editions of **Road Rash: 30th Anniversary Edition** (English). This is an English-only, GOG-compatible rework of [OBLPlife/RoadRash30th-HD](https://github.com/OBLPlife/RoadRash30th-HD).

**`RoadRash95.exe` is never modified.** The patch is a `ddraw.dll` proxy that the game loads from its own folder. It forwards all DirectDraw calls to the real system `ddraw.dll` and loads `RoadRashMultiFix.dll` at startup. The renderer checks the game code before hooking and does nothing on an executable it does not recognize (see `RoadRashHD.log`). Verified with the GOG build `60184028392664413` (SHA-256 `9e40e25737149ab095061b7154797a35495faea97644fafc5a1e616f1400c968`), which is byte-identical to the Steam build 26105967.

![The MultiFix launcher](docs/launcher.png)

## Install and Restore

1. Build `RoadRash_MultiFix_Setup.exe` (see below) and place it in the game folder, next to `RoadRash95.exe`.
2. Double-click the EXE and select **Install**. If the game is in `C:\Program Files` or `C:\Program Files (x86)`, right-click the EXE and choose **Run as administrator**.
3. Launch the game as usual (GOG Galaxy or the desktop shortcut). Resolution is detected automatically; adjust it under **Graphics and Sound → Race Resolution** if desired.

Install copies `ddraw.dll`, `RoadRashMultiFix.dll` and (if missing) `RoadRashHD.ini` into the game folder. If you already have a different `ddraw.dll` there, it is saved to `ResolutionPatch_Backup` and put back on **Restore**. To restore, open the same EXE and select **Restore**, or run it with `/restore`. `/quiet` suppresses dialogs. You can also remove the patch by simply deleting `ddraw.dll` and `RoadRashMultiFix.dll`.

### GOG notes

- GOG Galaxy's **Verify / Repair** will not undo the patch, because the original files are untouched. Galaxy may remove the extra files on a full reinstall; just run the installer again.
- Another `ddraw.dll` wrapper (dgVoodoo, cnc-ddraw, DDrawCompat, ...) cannot be used at the same time, because both use the same file name.

## Features

Inherited from the original project: automatic native resolution, native HD re-rasterization of roads and objects, aspect-ratio adaptation (standard / widescreen / ultrawide), in-game resolution selection, menu and rendering improvements.

Added in this fork:

- **No EXE modification:** loads through a `ddraw.dll` proxy; GOG Galaxy "Verify / Repair" does not undo it.
- **Installer and launcher:** detects GOG, EA app and Steam installations, shows the executable version, has one row per option (read from and saved to `RoadRashHD.ini` live), a **Play** button, detection of the MultiFix installation and its DLL version in the game folder (if it is missing or outdated, Play asks whether to install/update first; Yes or No, the game starts either way), backup/restore, HiDPI-safe layout and a custom icon.
- **Stutter Fix:** the controller scan that froze the game for ~165 ms every two seconds now runs in the background.
- **Fix Text Encoding:** Unicode text the game misreads (the trademark sign shown as `â„¢`, curly quotes, accents) is displayed correctly even with Windows' UTF-8 system code page.
- **Widescreen window, Alt+Enter / F11:** 16:9 window by default and borderless fullscreen toggle.
- **Cleaner textures in Direct3D:** thin far-away strips (water, road) and squeezed textures are averaged instead of aliasing into streaks; sprites are never filtered.
- **Sprite Position Interpolation:** far riders, cars and pedestrians are updated every frame instead of every 2-8 frames (`FullRateObjects`).
- **Direct3D 11 hardware renderer** (`D3D11=1`, default): the scene, textures, palette, text smoothing and presentation run on the GPU, with the CPU/GDI path as an automatic fallback. This makes **bilinear texture filtering** (`TextureFilter`, default on) practically free even at 4K.
- **Menus and text are never filtered**; texture filtering (`TextureFilter`) applies only to the 3D game environment and only in the Direct3D renderer. **Skip intro** (`SkipIntro`, off by default) keeps the menu music.

### v1.1.1 Animation Crash Fix

A missing-animation-metadata case caused crashes on the Peninsula track. **The original game's animation-frame selector also crashes with the same invalid input.** The update checks the animation resource type, skips invalid frames and adds regression tests.

## Configuration (Optional)

All options are in `RoadRashHD.ini` (game folder) and in the installer/launcher window; changes apply the next time the game starts. `1` = on, `0` = off.

| Section | Option | Default | What it does |
| --- | --- | :---: | --- |
| `[Graphics]` | `D3D11` | 1 | Direct3D 11 hardware renderer. Falls back to the CPU renderer automatically if it cannot start; `0` forces the CPU renderer. |
| `[Graphics]` | `TextureFilter` | 1 | Smooth (bilinear) textures of the 3D game environment in races (road, buildings, sky). Direct3D renderer only; menus and text are never filtered. |
| `[Graphics]` | `VSync` | 0 | Waits for the monitor refresh before showing each frame: no tearing, slightly more latency. |
| `[Window]` | `WideWindow` | 1 | Opens the game in a widescreen (16:9) window instead of the old 4:3 one. |
| `[Window]` | `AltEnterFullscreen` | 1 | Alt+Enter and F11 switch between window and borderless fullscreen. |
| `[Window]` | `StartFullscreen` | 0 | Starts in fullscreen instead of a window. |
| `[Text]` | `FixEncoding` | 1 | Fixes garbled special characters (for example `Rashâ„¢` instead of the trademark sign, or `â€œEnderâ€` instead of curly quotes). The game reads its strings with `LoadStringA`, which returns UTF-8 when Windows uses the "Use Unicode UTF-8 for worldwide language support" setting, while the game's bitmap font expects Windows-1252. |
| `[Input]` | `AsyncControllerScan` | 1 | Stutter fix: searches for controllers in a background thread. On the game thread it opened every HID device every two seconds, and one slow device froze the game for ~165 ms (credits, menus, races). |
| `[Game]` | `FullRateObjects` | 1 | Sprite position interpolation: far riders, cars and pedestrians are updated every frame instead of every 2-8 frames. |
| `[Game]` | `FixPostcards` | 1 | Repairs the broken left strip of the GAME LOADING picture. |
| `[Game]` | `SkipIntro` | 0 | Skips the studio logos and the intro movie; the menu music still plays. |

Advanced options, not shown in the launcher (add them to the ini):

- `[Graphics] RenderHeight=1080`: draws the 3D scene at a fixed height (here 1080 lines) and scales it to the window. Default `0` matches the window size, which is what you want in almost every case.
- `[Graphics] Profile=1`: writes frame-time statistics to `RoadRashHD.log`, for troubleshooting stutter.

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

## License

This project uses the [PolyForm Noncommercial License 1.0.0](LICENSE). Noncommercial use, modification and redistribution are permitted; **unauthorized commercial use and resale are prohibited**.
