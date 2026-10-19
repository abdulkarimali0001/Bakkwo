#!/bin/sh
# Removes 바꿔 Bakkwo installed by install.sh. Your converted files and settings are kept.
DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
rm -rf "$DATA/bakkwo" "$HOME/.local/bin/bakkwo" "$DATA/applications/bakkwo.desktop"
rm -f "$DATA/icons/hicolor/256x256/apps/bakkwo.png" "$DATA/icons/hicolor/512x512/apps/bakkwo.png"
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$DATA/applications" || true
echo "바꿔 Bakkwo removed."
