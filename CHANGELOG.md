# Changelog

## 2.20.5

**Fixes**
- **Keep Completed Tracks (2.20.0) removed; the victory screen no longer hangs.** The game asks whether all five tracks of the last level are done every time it goes to the home screen, shows the ending when they are, and clears the marks. Keeping them (2.20.0 put them back at once) made it show the ending again and again, and the game stayed on the victory screen after level 5. There is no way to keep the marks without that loop, so the game behaves as before: after the ending the marks are cleared. A leftover `KeepCompletedTracks` line in `RoadRashHD.ini` is ignored.
- **Animation crash** (one frame table with garbage in it, for example at mile 9.3 of the level 3 City course, `RoadRash95.exe+0x1bf8e`): that sprite is skipped instead of ending the game. The second frame selector of the game is guarded like the first one (the Peninsula fix of 1.1.1); a line in `RoadRashHD.log` says when it happened.
- **Police siren** was played at -90 to -100 dB (silent). New **Police Siren Loudness** slider (Audio tab, `[Audio] PoliceSirenLoudness`, default 80) removes that percentage of the attenuation.
- **Compatibility Loader** (launcher switch): loads MultiFix through `msimg32.dll` instead of the `ddraw.dll` proxy. On Windows 11 on ARM in a virtual machine the system DirectDraw refused to create the 8-bit surface of the race HUD when the proxy was in the game folder, and the game closed at the start of a race; with this loader Windows uses its own `ddraw.dll`. `RoadRashHD_proxy.log` records failed DirectDraw calls.

## 2.20.0

**Launcher**
- **Tabs, Stability first.** The options are grouped in tabs: Stability and QoL, Graphics, Display, Audio, Controls. DualSense Support is no longer marked experimental (a player confirmed it works). The Executable line recognises the verified executable with the 4 GB flag set, and the long note about unverified executables is gone. The footer text is gone.
- **4 GB Memory (Large Address Aware)**, a switch above the tabs. The game is a 32-bit program without that flag, so it has only 2 GB of memory for itself, the Direct3D driver and this patch; running out of it shows as missing textures and a crash after a few miles. The switch sets one flag in `RoadRash95.exe` (the only thing in MultiFix that changes the executable; off by default), keeps the original as `RoadRash95.exe.multifix-backup`, shows whether the file is patched, and Restore original (or unchecking it) puts the original back (only a backup that matches this very file is restored). Needs 64-bit Windows. Steam, GOG Galaxy or the EA app may restore the file when they verify the game; switch it on again then.
- **Netplay update check.** The launcher reads the installed OKPlay version from its readme and asks GitHub (the tags of Patrick-Working/okplay) for the newest one; a newer version is offered as "update netplay". The project has no downloadable releases on GitHub, so the zip still comes from oldkid.net.
- **Pedestrian Loudness slider** replaces the Louder Pedestrians check box (`[Audio] PedestrianLoudness`, default 67): pedestrians were reported too loud next to the rest of the game, so the default is a third lower than before (100 = the earlier level, 0 = the game's own levels).
- **Custom music help.** The Audio tab has a link "Custom music (WAV): how it works" that explains WAV Music Replacement, the `[MusicFiles]` names and the playlist (folders, formats, which file a track gets, what the log lines mean), and a link that opens the playlist folder (created when missing).
- **Distance to Finish** option (`[Game] DistanceToFinish`, off by default, Stability and QoL tab): the HUD distance counts down to the finish line (the course length of the track menu minus the distance driven) instead of counting up from the start. The odometer itself no longer wraps to 0 (see Stability).
- **Smooth Road** option (`[Game] SmoothRoad`, on by default): removes the half-pixel snapping of the road and its edges and adds back the fractions of elevation and bend that the game drops when it projects the track, so the road and the curbs do not jump or pulse.

**Stability**
- **Odometer fix** (always on, no switch). The HUD distance is computed in tenths of a mile (tenths of a kilometre with metric units) and the game kept it in one byte, so the display wrapped to 0 after 25.5 (in kilometres most courses are longer than that) and showed 25.x at a position before the start line. It is now computed in full and never goes below 0.
- Crash diagnostics: `RoadRashHD.log` gets the free address space of the game every 30 seconds, the first fatal exception (address, module, registers) and the number of polygons that could not be drawn. A polygon or texture whose memory is unreadable is skipped instead of crashing the game. The per-frame texture atlas is four times larger, so many animated sprites no longer make textures go missing. The sound log is limited to 600 lines.
- **Keep completed tracks** (`[Game] KeepCompletedTracks`, on by default). Winning the fifth track of the last level makes the game clear all five "track completed" marks of the save and show the ending, so a finished game looks as if no track had been completed. The marks now stay set. Side effect: with all five marks set the ending screen is probably shown again after each later win; `KeepCompletedTracks=0` is the game's own behaviour.
- **Frame diagnostics** in `RoadRashHD.log`: frames more than 25 ms apart are logged with their share of the game's own frame work and the place on the course, and every 5 seconds the log says how evenly the player moved along the track (frames without movement, steps far from the average). No effect on the game.
- Safer installation of the in-memory patches: every change to the game's code is journaled and undone if its group fails half way, and all signatures that can stop the installation are checked before the first byte is changed. The crash log skips only the deliberate memory probes of the DLL, so a real crash in the DLL is logged as well.

**Sound**
- **WAV Music Replacement** (`[Audio] MusicWavFiles`, off by default, launcher option in the Audio tab): a `MUSIC\NAME.WAV` next to `MUSIC\NAME.RRA` is played instead of it, so a CD-quality replacement (16-bit, 44.1 kHz) can be used. The game streams the music through a DirectSound buffer of 11 chunks of (samples per second x 16) / 11 bytes, which is not a whole number of sample frames for 16-bit 44.1 kHz (64145 bytes, frame 4): the buffer held one byte too many, the stream slipped at every wrap and became white noise a few seconds in. The chunk is now rounded down to whole sample frames. No bigger buffer is needed. Any file name works through the `[MusicFiles]` section of `RoadRashHD.ini` (`T_TEETH=my remaster.wav`: a name in `AUDIO\MUSIC`, a path relative to the game folder or a full path; the 14 track names are listed in the ini). Files that are not PCM 8/16-bit mono/stereo WAVs (checked for a complete header and data) are skipped and the original plays. **Music Playlist** (`[Audio] MusicPlaylist`, off by default, launcher option): every WAV in `AUDIO\MUSIC\Playlist` (or `MusicPlaylistFolder`) is music for the game, in random order without repeats; tracks with their own `[MusicFiles]` line or `TRACK.WAV` keep it.
- **Muted Sound Fix** (`[Audio] MidiVolumeGuard`), now also a level control for the MIDI music (the 2.18.6 version only kept the game from silencing itself). Two problems with the MIDI music and the Sound Mix slider. (1) With the music slider at 0 the game asked Windows to set its MIDI volume to 0. On some Windows versions that call is applied to the whole game in the Windows volume mixer, so the game went completely silent (music, effects and videos) and Windows remembered it, also after a restart. (2) Many Windows MIDI devices ignore the volume the game sets, so the slider did nothing for the MIDI music (for example when the rock music is off): even at 0 % it kept playing. What changed: the game's MIDI volume is no longer sent to Windows at all, so the level of the game in the volume mixer stays untouched. The level of the Sound Mix screen (master x game music) is applied to the MIDI notes themselves: 0 % is silent, 5 % plays at 10 % of the note velocity, and the louder the slider the louder the music, up to a ceiling at 100 % (`[Audio] MidiMaxPercent`, default 70, so that 100 % is not too loud; 100 plays the original velocities). If the game is already stuck at zero, raise its slider once in Windows Settings > System > Sound > Volume mixer.

## 2.18.6

**Sound**
- **Muted Sound Fix** (`[Audio] MidiVolumeGuard`, on by default, launcher option). What was wrong: with the music slider at 0 in the Sound Mix screen the game asked Windows to set its MIDI volume to 0. On some Windows versions that call is applied to the whole game in the Windows volume mixer, so the game went completely silent (music, effects and videos) and Windows remembered it, also after a restart. What changed: a silent MIDI volume is no longer passed on to Windows; the music is muted by the game itself and the game's level in the volume mixer stays untouched. If the game is already stuck at zero, raise its slider once in Windows Settings > System > Sound > Volume mixer.
- **FMV Volume fix** (always on, no switch). The FMV Volume slider works by scaling the game's volume in the Windows volume mixer while a video plays and restoring it afterwards. If the game was closed in the middle of a video (Alt+F4, a crash), the scaled level stayed and Windows started the game quieter next time. The original level is now remembered and restored at the next start, and a video that starts while another one is scaled no longer scales the level a second time.

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
