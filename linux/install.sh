#!/usr/bin/env bash
# RoadRash30-MultiFix - manual install for Linux, Steam Deck and Proton/Wine (EXPERIMENTAL, untested).
# Copies ddraw.dll and RoadRashMultiFix.dll next to RoadRash95.exe. RoadRash95.exe itself is never modified.
#
#   ./install.sh "/path/to/Road Rash 30th Anniversary Edition"            install / update
#   ./install.sh "/path/to/Road Rash 30th Anniversary Edition" --remove   remove again
#
# Steam: Properties -> General -> Launch Options:
#   WINEDLLOVERRIDES="ddraw=n,b" %command%
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
game="${1:-}"
action="${2:-install}"

if [[ -z "$game" || ! -f "$game/RoadRash95.exe" ]]; then
    echo "Usage: $0 <game folder containing RoadRash95.exe> [--remove]" >&2
    exit 1
fi

if [[ "$action" == "--remove" ]]; then
    rm -f "$game/RoadRashMultiFix.dll"
    if [[ -f "$game/ddraw.dll.multifix-backup" ]]; then
        mv -f "$game/ddraw.dll.multifix-backup" "$game/ddraw.dll"
    else
        rm -f "$game/ddraw.dll"
    fi
    echo "MultiFix removed. (RoadRashHD.ini and RoadRashHD.log were kept.)"
    exit 0
fi

for f in ddraw.dll RoadRashMultiFix.dll; do
    [[ -f "$here/$f" ]] || { echo "Missing $f next to this script." >&2; exit 1; }
done

# keep a different ddraw.dll that was already there
if [[ -f "$game/ddraw.dll" ]] && ! cmp -s "$game/ddraw.dll" "$here/ddraw.dll" && [[ ! -f "$game/ddraw.dll.multifix-backup" ]]; then
    cp -p "$game/ddraw.dll" "$game/ddraw.dll.multifix-backup"
    echo "Existing ddraw.dll saved as ddraw.dll.multifix-backup"
fi
cp -f "$here/ddraw.dll" "$here/RoadRashMultiFix.dll" "$game/"
[[ -f "$game/RoadRashHD.ini" ]] || { [[ -f "$here/RoadRashHD.ini" ]] && cp "$here/RoadRashHD.ini" "$game/"; }

cat <<MSG
MultiFix installed in: $game

Set this Steam launch option (Properties -> General -> Launch Options):

    WINEDLLOVERRIDES="ddraw=n,b" %command%

Outside Steam, run the game with the same variable, for example:
    WINEDLLOVERRIDES="ddraw=n,b" wine RoadRash95.exe

If something looks wrong, set  D3D11=0  in RoadRashHD.ini (CPU renderer) and send RoadRashHD.log.
MSG
