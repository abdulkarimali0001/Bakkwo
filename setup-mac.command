#!/bin/bash
# One-time setup for building Bakkwo on macOS: installs the .NET 8 SDK into ~/.dotnet
# (no admin password needed) and FFmpeg via Homebrew if Homebrew is present.
cd "$(dirname "$0")"
LOG="setup-log.txt"
{
  echo "== $(date)"
  echo "== Installing .NET 8 SDK into ~/.dotnet"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && bash /tmp/dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet" \
    || echo "!! .NET install failed"

  # Make the new SDK the default in new Terminal windows (added once).
  if ! grep -qE 'BAKKWO_DOTNET|BADILI_DOTNET' "$HOME/.zprofile" 2>/dev/null; then
    printf '\n# BAKKWO_DOTNET: .NET 8 SDK installed by Bakkwo setup\nexport DOTNET_ROOT="$HOME/.dotnet"\nexport PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"\n' >> "$HOME/.zprofile"
    echo "== Added ~/.dotnet to PATH in ~/.zprofile"
  fi
  export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH"

  echo "== Installing FFmpeg (audio/video support)"
  BREW=""
  for b in /opt/homebrew/bin/brew /usr/local/bin/brew; do [ -x "$b" ] && BREW="$b" && break; done
  if [ -n "$BREW" ]; then
    "$BREW" list ffmpeg >/dev/null 2>&1 && echo "ffmpeg already installed" || "$BREW" install ffmpeg
  else
    echo "!! Homebrew not found, skipping FFmpeg. Install Homebrew from https://brew.sh, then run: brew install ffmpeg"
  fi

  echo "== Versions"
  dotnet --list-sdks
  command -v ffmpeg >/dev/null && ffmpeg -version | head -1 || echo "ffmpeg: not installed"

  echo "== Building and testing Bakkwo"
  dotnet test 2>&1 | tail -15
  echo "== EXIT $?"
} 2>&1 | tee "$LOG"
echo "== DONE" >> "$LOG"
echo
read -n 1 -s -r -p "Setup finished. Press any key to close."
