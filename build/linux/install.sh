#!/bin/sh
# Installs 바꿔 Bakkwo for the current user (no root needed):
#   ~/.local/share/bakkwo         the app
#   ~/.local/bin/bakkwo           a launcher on your PATH
#   app menu entry + icon, and "Open With" for the file types Bakkwo converts.
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
DEST="$DATA/bakkwo"

echo "Installing 바꿔 Bakkwo to $DEST"
rm -rf "$DEST"
mkdir -p "$DEST"
cp -R "$HERE/app/." "$DEST/"
chmod +x "$DEST/Bakkwo"

mkdir -p "$HOME/.local/bin"
ln -sf "$DEST/Bakkwo" "$HOME/.local/bin/bakkwo"

for size in 256 512; do
  mkdir -p "$DATA/icons/hicolor/${size}x${size}/apps"
  cp "$HERE/icons/bakkwo-$size.png" "$DATA/icons/hicolor/${size}x${size}/apps/bakkwo.png"
done

mkdir -p "$DATA/applications"
sed "s|@EXEC@|$DEST/Bakkwo|" "$HERE/bakkwo.desktop" > "$DATA/applications/bakkwo.desktop"

command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$DATA/applications" || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t "$DATA/icons/hicolor" || true

echo "Done. Open it from your app menu (search \"Bakkwo\" or \"바꿔\"), or run: bakkwo"
case ":$PATH:" in *":$HOME/.local/bin:"*) ;; *) echo "Note: add ~/.local/bin to your PATH to use the 'bakkwo' command." ;; esac
command -v ffmpeg >/dev/null 2>&1 || echo "Tip: install FFmpeg for audio and video (e.g. sudo apt install ffmpeg)."
command -v soffice >/dev/null 2>&1 || echo "Tip: install LibreOffice for DOC/RTF/ODT and Word → PDF (e.g. sudo apt install libreoffice-writer)."
