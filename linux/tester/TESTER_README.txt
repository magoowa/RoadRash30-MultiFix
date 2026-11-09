RoadRash30-MultiFix 2.13.7 - Proton / Wine TEST package
=======================================================

Thank you for testing! This is an experimental build: nobody has run it under Wine/Proton yet.
It writes a detailed log on purpose.

INSTALL
  1. Unpack this folder anywhere.
  2. ./install.sh "/path/to/Road Rash 30th Anniversary Edition" --overwrite-ini
     (the folder that contains RoadRash95.exe; for Steam it is under steamapps/common)
  3. Steam: Properties -> General -> Launch Options:
         WINEDLLOVERRIDES="ddraw=n,b" %command%
     Without Steam:  WINEDLLOVERRIDES="ddraw=n,b" wine RoadRash95.exe
  4. Start the game. Use the Proton version you normally use and note its name (for example "Proton 9.0-4").

WHAT TO TRY (about 5 minutes is enough)
  - Does the game start? Does the menu show?
  - Start a race (any track). Does the 3D picture look right (road, sky, HUD)?
  - Press Alt+Enter: window <-> fullscreen. Is the picture still complete (HUD visible, not cropped)?
  - Quit the race with Esc, go back to the menu. Does the menu still draw?

WHAT TO SEND BACK (all in the game folder)
  - RoadRashHD.log                     (always, even if the game did not start)
  - RoadRashHD_frame_0.bmp ... _3.bmp  (the first few; zip them)
  - Your system: distro, GPU + driver, Proton/Wine version, Steam Deck or desktop, Wayland or X11.
  - A sentence on what looked wrong, and when.

If there is NO RoadRashHD.log at all, the ddraw override did not take effect (the proxy was not loaded).
Check the launch option, and that ddraw.dll sits next to RoadRash95.exe.

IF IT LOOKS WRONG, try these one at a time (edit RoadRashHD.ini in the game folder, restart the game,
and send the new log for each):
  A. [Graphics] D3D11=0              CPU renderer instead of Direct3D 11
  B. [Graphics] ChildSurface=1       use the Windows swap-chain arrangement (child window)
  C. [Window]   WideWindow=0         original 4:3 window

REMOVE
  ./install.sh "/path/to/Road Rash 30th Anniversary Edition" --remove
