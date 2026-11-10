#!/usr/bin/env bash
# RoadRash30-MultiFix - install for Linux, Steam Deck and Proton/Wine (EXPERIMENTAL).
# Copies ddraw.dll and RoadRashMultiFix.dll next to RoadRash95.exe. RoadRash95.exe itself is never modified.
#
#   ./install.sh                                                       finds the Steam copy by itself
#   ./install.sh "/path/to/Road Rash 30th Anniversary Edition"         install / update in that folder
#   ./install.sh "/path/to/Road Rash 30th Anniversary Edition" --remove   remove again
#   ./install.sh "/path/to/Road Rash 30th Anniversary Edition" --overwrite-ini   also replace RoadRashHD.ini
#
# IMPORTANT: copying the files is not enough. Wine/Proton only loads our ddraw.dll when this Steam launch option is set
# (Properties -> General -> Launch Options):
#   WINEDLLOVERRIDES="ddraw=n,b" %command%
set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
game="${1:-}"
action="${2:-install}"
LAUNCH='WINEDLLOVERRIDES="ddraw=n,b" %command%'

# Show a message in the terminal; when there is no terminal (double-click in the file manager) also in a window.
say() {
    printf '%s\n' "$*"
    if [[ ! -t 1 ]]; then
        if command -v kdialog >/dev/null 2>&1; then kdialog --title "RoadRash30-MultiFix" --msgbox "$*" 2>/dev/null
        elif command -v zenity >/dev/null 2>&1; then zenity --info --title "RoadRash30-MultiFix" --no-markup --text "$*" 2>/dev/null
        elif command -v xmessage >/dev/null 2>&1; then xmessage -center "$*" 2>/dev/null
        fi
    fi
}
fail() { say "$*" >&2; exit 1; }

# No folder given: look for the game in the usual Steam libraries (internal storage, SD card, Flatpak Steam).
find_game() {
    local roots=("$HOME/.local/share/Steam/steamapps/common" "$HOME/.steam/steam/steamapps/common" "$HOME/.steam/root/steamapps/common"
                 "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common" "$HOME/Games" "$HOME/Heroic")
    local r
    for r in /run/media/*/steamapps/common /run/media/*/*/steamapps/common /media/*/steamapps/common /mnt/*/steamapps/common; do roots+=("$r"); done
    local seen="" found=() root exe dir
    for root in "${roots[@]}"; do
        [[ -d "$root" ]] || continue
        while IFS= read -r exe; do
            dir="$(dirname "$exe")"; dir="$(cd "$dir" && pwd -P)"
            case "$seen" in *"|$dir|"*) continue;; esac
            seen="$seen|$dir|"; found+=("$dir")
        done < <(find "$root" -maxdepth 4 -name RoadRash95.exe 2>/dev/null)
    done
    printf '%s\n' "${found[@]}"
}

if [[ -z "$game" ]]; then
    mapfile -t candidates < <(find_game)
    if [[ ${#candidates[@]} -eq 1 && -n "${candidates[0]}" ]]; then
        game="${candidates[0]}"; echo "Found the game in: $game"
    elif [[ ${#candidates[@]} -gt 1 && -n "${candidates[0]}" ]]; then
        if [[ -t 0 ]]; then
            echo "More than one copy of the game was found:"; i=1
            for c in "${candidates[@]}"; do echo "  $i) $c"; i=$((i+1)); done
            read -r -p "Number: " n
            game="${candidates[$((n-1))]:-}"
        else
            fail "More than one copy of the game was found. Run this script in a terminal and give the folder:
./install.sh \"/path/to/Road Rash 30th Anniversary Edition\""
        fi
    elif [[ -t 0 ]]; then
        read -r -p "Game folder (the one that contains RoadRash95.exe): " game
    fi
fi
game="${game%/}"
if [[ -z "$game" || ! -f "$game/RoadRash95.exe" ]]; then
    fail "RoadRash95.exe was not found. Give the game folder (the one that contains RoadRash95.exe):
./install.sh \"/path/to/Road Rash 30th Anniversary Edition\"
In Steam: right-click the game > Manage > Browse local files shows that folder."
fi

if [[ "$action" == "--remove" ]]; then
    rm -f "$game/RoadRashMultiFix.dll"
    if [[ -f "$game/ddraw.dll.multifix-backup" ]]; then
        mv -f "$game/ddraw.dll.multifix-backup" "$game/ddraw.dll"
    else
        rm -f "$game/ddraw.dll"
    fi
    say "MultiFix removed. (RoadRashHD.ini and RoadRashHD.log were kept.) You can clear the Steam launch option too."
    exit 0
fi

for f in ddraw.dll RoadRashMultiFix.dll; do
    [[ -f "$here/$f" ]] || fail "Missing $f next to this script. Unpack the whole linux folder first."
done

# keep a different ddraw.dll that was already there
if [[ -f "$game/ddraw.dll" ]] && ! cmp -s "$game/ddraw.dll" "$here/ddraw.dll" && [[ ! -f "$game/ddraw.dll.multifix-backup" ]]; then
    cp -p "$game/ddraw.dll" "$game/ddraw.dll.multifix-backup"
    echo "Existing ddraw.dll saved as ddraw.dll.multifix-backup"
fi
cp -f "$here/ddraw.dll" "$here/RoadRashMultiFix.dll" "$game/" || fail "Could not copy the files into $game (is the folder writable?)."
if [[ -f "$here/RoadRashHD.ini" ]] && { [[ ! -f "$game/RoadRashHD.ini" ]] || [[ "$action" == "--overwrite-ini" ]]; }; then
    cp -f "$here/RoadRashHD.ini" "$game/"
fi

say "MultiFix files are in: $game

LAST STEP (required): in Steam open the game's Properties > General > Launch Options and paste:

    $LAUNCH

Without this launch option Proton ignores the new ddraw.dll and the game starts without MultiFix.
Outside Steam run the game with:  WINEDLLOVERRIDES=\"ddraw=n,b\" wine RoadRash95.exe

After the first start, RoadRashHD.log appears in the game folder. If something looks wrong, set D3D11=0 in
RoadRashHD.ini (CPU renderer) and send the log."
