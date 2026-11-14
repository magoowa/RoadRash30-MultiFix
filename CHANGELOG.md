# Changelog

## 2.18.0

**Sound**
- **New pedestrian voices and collision sounds** (`[Audio] PedestrianVoiceRules`, on by default, launcher option): every kind of pedestrian shouts with its own pool of voices, one drawn at random for the first and the second reaction (table in `PedestrianVoices.ini`, which you can edit). The flag girls and the people standing at the start react to a collision as well, a woman who had no voice in the game's data now has one (`FemaleVoiceFallback`), and two first reactions keep at least 120 ms apart (`PedestrianVoiceGapMs`) so a fight is not a chorus.
- **Louder pedestrians** (`[Audio] LouderPedestrians`, `PedestrianGainDb`, `PedestrianVolumeRelief`, `PedestrianVoiceRelief`; launcher option): the game plays pedestrian voices and footsteps at a fixed -17.6 dB, far below the rider. They are louder now (louder copies are made once in `%LOCALAPPDATA%\RoadRashMultiFix\audio`; the game's own files are never changed) and the voices are no longer drowned by the crash and grunt.

**Picture**
- **Sprite fixes** (`[Game] SpriteFixes`, on by default, launcher option): the damaged shadow of the first punch frame (drawn far from the bike in the original game) is cut to its real size, and the buildings that hovered above the ground (red barn, brown wooden shop, white house) are lowered (`LowerRedBarn`, `LowerBrownShop`, `LowerWhiteHouse`). Only the drawing changes, in memory.

**Controllers and window**
- **A native DualSense no longer scrolls the menus by itself** (always on): the game opened every HID interface of the pad, including ones that are not a game pad; only game pad and joystick interfaces are opened now. Found by CypherCripto (issue #4).
- **DualSense support** (`[Input] DualSense`, off by default, launcher option): Bluetooth reports and d-pad steering and menus, diagonals too. Written from the report and checked against the game code; not tested without a DualSense. Skipping the intro videos with a pad that is not an XInput one (DualSense, DualShock) works too.
- **Analog throttle on the right trigger** (`[Input] AnalogThrottle` and `Throttle*`): the throttle follows the pressure of the trigger, so slow speeds can be held.
- **Back to the game with the pad** (always on): after Alt+Tab or a click on another monitor the game stays in the background and only the keyboard or mouse could bring it back. While the game is not the active window, Start + Back on an Xbox pad restores it and takes the focus; nothing changes while you play.
- **Dual monitor UX improvements** (`[Window] DualMonitorUX`, off by default, launcher option): the mouse is not confined to the game window and the game is not minimized when you click another monitor.

**Proton, Wine and Linux** (no switches, always on)
- The `ddraw.dll` proxy loads the system ddraw once and thread-safely, finds its functions by name (Wine's ordinals differ), ends a missing export in a safe failure and writes nothing in `DllMain`; under Wine it writes `RoadRashHD_proxy.log`. This fixes the crash reported after the first `DirectDrawCreate` (to be confirmed on Wine).
- `linux/install.sh`: `--remove` and `--overwrite-ini` work in any position, `--remove` never deletes a `ddraw.dll` that is not ours, a foreign `ddraw.dll` is never overwritten when an older backup exists, and a failing dialog tool no longer changes the exit code.

**Other**
- **The `ddraw.dll` proxy (the DirectDraw loader) was made safer.** MultiFix reaches the game through a small `ddraw.dll` placed next to `RoadRash95.exe`: the game loads it instead of the system one, it starts MultiFix and forwards every DirectDraw call to the real system `ddraw.dll`. The review of 2.18.0 found weak spots in that forwarding, which matter most under Proton, Wine and CrossOver (and are harmless on Windows): the real `ddraw.dll` is now loaded exactly once and thread-safely, so a second thread can no longer jump through a half-filled table; its functions are found by name, because Wine's ordinal numbers differ from Windows'; an export that the real `ddraw.dll` does not have ends in a safe error instead of a jump to address zero; and nothing is written to a file while the DLL loads (Windows holds a lock then). A new test calls the proxy from 16 threads at once (`hd_proxy_threads_test`).
- **If something goes wrong, please send the logs.** `RoadRashHD.log` (next to the game) is written by MultiFix at every start: it lists what was patched and why something was skipped. If it does not appear at all, MultiFix was not loaded (under Proton the launch option `WINEDLLOVERRIDES="ddraw=n,b" %command%` is required). Under Wine, Proton and CrossOver the proxy also writes `RoadRashHD_proxy.log`, which shows how the real `ddraw.dll` was found. `[Debug] Profile=1` adds frame-time statistics, `QuadDiagnostics=1` saves frames with picture glitches as `RoadRashHD_glitch_N.bmp`, and `[Graphics] D3D11=0` switches to the CPU renderer to tell a Direct3D problem from another one. Attach both logs, `RoadRashHD.ini` and your system (Windows version, or Proton / Wine / CrossOver version) to the report.
- A failed Direct3D `Present` (other than device loss, which already switched the renderer off) is now written to the log and that frame is skipped, instead of being treated as success; this makes problems under DXVK visible in `RoadRashHD.log`.
- All logs and troubleshooting keys are in one `[Debug]` section (`Profile`, `DumpFrameEverySeconds`, `DumpScale`, `QuadDiagnostics`, `CompareGpu`, `KeyScript`); old values in `[Graphics]` are still read.

## 2.17.3

- **Keyboard + Controller Together** (`[Input] KeyboardAndPad`, launcher option, on by default): the game read only the control device saved in the profile, so a profile set to the keyboard ignored the pad in a race. Both are read now, and missing pad assignments of a keyboard profile are filled in with the game's defaults.
- **Hotkeys fixed:** F11, Alt+Enter and Alt+F4 also work when the key message does not reach the game window (the keys are watched as well).
- **Videos follow the window:** after F11 / Alt+Enter the game is told whether it is windowed, so videos play in the window and not over the whole screen. A window made from fullscreen is 4:3 when Widescreen is off.
- **Widescreen Game switch fixed:** with it off (`[Window] WideWindow=0`) the game now plays in the original 4:3 aspect.
- **Load Game remembers the last save** (`[Game] RememberLastSave`, on by default): the Open dialog starts with the save you used last already selected (the name is kept in `[Game] LastSave` after every Open or Save as).


## 2.16.0

- **High Performance mode** (`[Performance] HighPerformance`, launcher option, on by default): Windows power throttling (EcoQoS) off for the game, 1 ms timer, priority slightly above normal. Meant for laptops and integrated GPUs; no measurable difference on a machine that already held 60 fps.
- **File properties:** `RoadRashMultiFix.dll` and `ddraw.dll` now carry a description, product name, version, copyright and project link in their Details tab.
- **Linux / Steam Deck:** `install.sh` finds the Steam copy of the game by itself (internal storage, SD card, Flatpak Steam), shows its messages in a window when it is started without a terminal, and states clearly that the Steam launch option `WINEDLLOVERRIDES="ddraw=n,b" %command%` is required. The README explains the manual way too.
- **Fully commented ini:** `RoadRashHD.ini` now lists every setting (also the advanced ones: `Enabled`, `RenderHeight`, `ChildSurface`, `Profile`, `Width`/`Height`, ...) with its default and an explanation. A new ini is created from it, and when an older ini is updated the missing keys are added with the same comments, in the right section; existing values and your own lines are not touched.
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
- **Launcher:** new logo and icon, language selector as a row of radio buttons under the MultiFix line, aligned layout, controller options grouped above the cursor option, consistent option names, the description moved to the bottom, and a clearer "Widescreen Game" option that says the whole game runs in widescreen, not only the window at start.
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
