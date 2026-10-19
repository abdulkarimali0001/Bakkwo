#!/bin/bash
# Builds Bakkwo and runs its tests. Double-click in Finder, or run ./build.command
cd "$(dirname "$0")"
export PATH="$HOME/.dotnet:/usr/local/share/dotnet:/opt/homebrew/bin:/usr/local/bin:$PATH"
LOG="build-log.txt"
{
  echo "== $(date)"
  if ! command -v dotnet >/dev/null; then
    echo "dotnet was not found. Install the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
  fi
  echo "== SDKs:"; dotnet --list-sdks
  if ! dotnet --list-sdks | grep -qE '^(8|9|[1-9][0-9])\.'; then
    echo
    echo "Bakkwo needs the .NET 8 SDK (or newer), and only older SDKs are installed."
    echo "Install it with:  brew install --cask dotnet-sdk"
    echo "or download the Arm64 installer: https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
  fi
  echo "== Test"
  dotnet test 2>&1
  echo "== EXIT $?"
} 2>&1 | tee "$LOG"
echo "== DONE" >> "$LOG"
echo
read -n 1 -s -r -p "Press any key to close."
