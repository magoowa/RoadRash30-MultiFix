# Changelog

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
