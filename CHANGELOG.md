# Changelog

## 2.16.0

- **High Performance mode** (`[Performance] HighPerformance`, launcher option, on by default): Windows power throttling (EcoQoS) off for the game, 1 ms timer, priority slightly above normal. Meant for laptops and integrated GPUs; no measurable difference on a machine that already held 60 fps.
- **Dialog look:** the game's own Open/Save dialogs and message boxes, and dialogs of other DLLs shown from the game thread (the netplay chooser), use the themed Windows look: the DLL carries a visual-styles (Common-Controls 6) manifest that is switched on for the game thread. The dialogs themselves are unchanged.
- **Windowed mode from the game menu:** choosing "Windowed" in the game menu no longer shrinks the window to 4:3; it gets the size from `[Window] Width/Height` (or the automatic 16:9 size) like at start-up.
- **Intel iGPU Battery Boost** (`[Performance] LowLatency=0`, launcher option, on by default): performance boost on battery-powered Intel iGPU. With `LowLatency=1` (waitable swap chain with one queued frame plus a tearing present) an Intel Iris Xe laptop on battery dropped from 60 to 45 fps (frame interval 16.7 to 22.2 ms, present about 17 ms instead of 0.9 ms); with it off the game ran at a steady 60 fps. The launcher check box is the inverse of the ini key.
- **Quality presets** (`[Graphics] Quality`, ini only): High / Medium (720p) / Low (540p) / Minimum (480p) / Auto. Auto lowers the render height for the next start after two slow windows of about 600 frames; a fixed `RenderHeight` wins.
- **GPU time in the profile log:** with `Profile=1` the log line shows the GPU milliseconds per frame (avg, p95, max), measured with timestamp queries.
- **Smooth text and interface** (`[Graphics] SmoothText`, ini only, off by default): bilinear filtering of the text and the interface. The menus are scaled to the window with bilinear sampling instead of nearest (the smoothed frame is cached and rebuilt only when the menu picture changes), and the race HUD and text layer is drawn with the smoothing shader of the Direct3D renderer. Off, the text stays crisp as before.
- **Launcher layout:** the logo moved to the top left, the installations list, executable, MultiFix, language and netplay lines are next to it, the game folder box is gone (the list shows it), and the authors and contributions are listed above the buttons. One font and one margin everywhere, so all blocks line up.
- **Netplay detection:** the launcher checks for `wsock32.dll` in the game folder (OKPlay) and shows its datecode; when it is missing it shows a **download netplay** link. That downloads the newest OKPlay package from https://oldkid.net/aoe2net/latest.php and unpacks `netcheck.exe`, `okplay.ini` (an existing one is kept), `wsock32.dll` and `readme.txt` (saved as `oldkid_netplay_readme.txt`) into the game folder. If oldkid.net fails, the OKPlay GitHub release is tried; if that fails too, the launcher shows the error and a link to https://github.com/Patrick-Working/okplay/.
- **Update check:** the launcher asks GitHub for the latest MultiFix release in the background and, if it is newer than the installed version, shows a clickable message with the version number. Clicking the logo opens the project page.

## 2.15.0

- **FMV Volume** (`[Game] VideoVolume`, launcher slider): sets the volume of all videos (.avi and .mp4) from 0 to 100 % of the game's volume. The game's audio session volume is scaled while a video plays and restored afterwards.
- **Fix Bike Colors** (`[Game] FixBikeColors`): the Rat and Super bikes had each other's colours in the race; the 3D bikes now match the showroom. Bug found by Anakin94 / peemosa, fixed here in the DLL without touching the exe.

## 2.14.2

**New**
- **Symmetric Analog Steering** (`[Input] SymmetricSteering`): with an analog stick the right turn was about twice as sensitive as the left one, started with a jump and overshot by 25 %. Both directions now use the same curve.
- **Controller answers dialogs** (`[Input] PadDialogs`): A = OK / Yes and B = Cancel / No in the game's message boxes (for example the quit confirmation); the D-pad or left stick moves between buttons.
- **Controller skips videos:** A, B, X, Y, Start or Back skip the intro and other videos.
- **Race Resolution dialog works with the controller** (D-pad changes the resolution, A applies, B cancels). It opens once, and the game's menu no longer reacts to the same button presses behind it.
- **Auto-hide mouse cursor** (`[Window] AutoHideCursor`): the cursor is hidden from the first window, comes back when the mouse moves, hides again after 3 s, does not flicker in the menus, and stays visible in the "Save as..." dialog and message boxes.
- **Language selection** (`[Game] Language`): the launcher lists the languages found in the TEXT folder and saves the choice to the ini; no administrator rights or registry editing needed.
- **Start in Fullscreen is on by default** for new installations.
- **Launcher:** new logo and icon, language selector as a row of radio buttons under the MultiFix line, aligned layout, controller options grouped above the cursor option, consistent option names, the description moved to the bottom, and a clearer "Widescreen (16:9) Game" option that says the whole game runs in widescreen, not only the window at start.
- The installer file name now contains the version (`RoadRash_MultiFix_Setup_v2.14.2.exe`).

**Changed**
- Removed leftovers of unfinished experiments (object fade-in, UI text smoothing, CPU texture filtering, one-off debug logs). The CPU renderer buffers are created only when the CPU renderer is used (about 25 MB less memory at 4K with Direct3D). The DLL is about 12 KB smaller.
- Direct3D shaders are compiled at build time (no shader compiler is needed at run time); Linux/Proton install script and test package (experimental).

## 2.13.7

First public release of the MultiFix fork of [OBLPlife/RoadRash30th-HD](https://github.com/OBLPlife/RoadRash30th-HD):

- Loads through a `ddraw.dll` proxy; `RoadRash95.exe` is never modified. GOG, EA app and Steam support.
- Stutter Fix (background controller scan), Sprite Position Interpolation (far objects every frame), Fix Text Encoding, Skip Intro Videos, LOADING screen fix.
- Direct3D 11 hardware renderer with texture filtering for the 3D scene (sprites, menus and text are never filtered), thin far strips (water, road) blended instead of streaking, CPU/GDI fallback.
- Widescreen window, Alt+Enter / F11 fullscreen toggle (scene scaling follows the window), Alt+F4 always works.
- Installer and launcher: edition and executable detection, MultiFix and DLL version detection, Play button that offers to install first, backup and restore of replaced files, live reading and writing of `RoadRashHD.ini`.
