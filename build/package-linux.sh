#!/bin/bash
# Builds publish/Bakkwo-<version>-linux-<arch>.tar.gz: the self-contained app plus install.sh.
#   ./build/package-linux.sh              # x64
#   RID=linux-arm64 ./build/package-linux.sh
set -euo pipefail
cd "$(dirname "$0")/.."

RID="${RID:-linux-x64}"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)"
NAME="Bakkwo-$VERSION-$RID"
STAGE="publish/$NAME"

rm -rf "$STAGE" "publish/$NAME.tar.gz"
dotnet publish src/Bakkwo.App -c Release -r "$RID" --self-contained -o "$STAGE/app" ${NUGET_CONFIG:+--configfile "$NUGET_CONFIG"}
rm -f "$STAGE/app/"*.pdb
rm -rf "$STAGE/app/Assets"

mkdir -p "$STAGE/icons"
cp src/Bakkwo.App/Assets/icon-256.png "$STAGE/icons/bakkwo-256.png"
cp src/Bakkwo.App/Assets/icon-512.png "$STAGE/icons/bakkwo-512.png"
cp build/linux/install.sh build/linux/uninstall.sh build/linux/bakkwo.desktop "$STAGE/"
chmod +x "$STAGE/install.sh" "$STAGE/uninstall.sh" "$STAGE/app/Bakkwo"
cp LICENSE THIRD-PARTY-NOTICES.md "$STAGE/"

tar -C publish -czf "publish/$NAME.tar.gz" "$NAME"
echo "Built publish/$NAME.tar.gz"
