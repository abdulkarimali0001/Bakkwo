#!/bin/bash
# Builds a self-contained 바꿔 Bakkwo.app for Apple Silicon (or Intel with RID=osx-x64).
#
#   ./build/package-mac.sh                 # → publish/Bakkwo.app
#   RID=osx-x64 ./build/package-mac.sh
#
# The bundle carries the app icon, an English + Korean display name (바꿔 when macOS is in Korean),
# and document types so Finder lists Bakkwo under "Open With" for the formats it converts.
set -euo pipefail
cd "$(dirname "$0")/.."

RID="${RID:-osx-arm64}"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)"
OUT="publish"
APP="$OUT/Bakkwo.app"
CONTENTS="$APP/Contents"

rm -rf "$APP" "$OUT/bin"
dotnet publish src/Bakkwo.App -c Release -r "$RID" --self-contained -p:UseAppHost=true -o "$OUT/bin" ${NUGET_CONFIG:+--configfile "$NUGET_CONFIG"}

mkdir -p "$CONTENTS/MacOS" "$CONTENTS/Resources/en.lproj" "$CONTENTS/Resources/ko.lproj"
cp -R "$OUT/bin/." "$CONTENTS/MacOS/"
rm -f "$CONTENTS/MacOS/"*.pdb "$CONTENTS/MacOS/Bakkwo.icns"
rm -rf "$CONTENTS/MacOS/Assets"
cp src/Bakkwo.App/Assets/Bakkwo.icns "$CONTENTS/Resources/Bakkwo.icns"
cp LICENSE THIRD-PARTY-NOTICES.md "$CONTENTS/Resources/"

printf '"CFBundleDisplayName" = "Bakkwo";\n"CFBundleName" = "Bakkwo";\n' > "$CONTENTS/Resources/en.lproj/InfoPlist.strings"
printf '"CFBundleDisplayName" = "바꿔";\n"CFBundleName" = "바꿔";\n' > "$CONTENTS/Resources/ko.lproj/InfoPlist.strings"

cat > "$CONTENTS/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Bakkwo</string>
  <key>CFBundleDisplayName</key><string>Bakkwo</string>
  <key>CFBundleIdentifier</key><string>com.khamisali.bakkwo</string>
  <key>CFBundleVersion</key><string>${VERSION}</string>
  <key>CFBundleShortVersionString</key><string>${VERSION}</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleSignature</key><string>BKWO</string>
  <key>CFBundleExecutable</key><string>Bakkwo</string>
  <key>CFBundleIconFile</key><string>Bakkwo</string>
  <key>CFBundleDevelopmentRegion</key><string>en</string>
  <key>CFBundleLocalizations</key><array><string>en</string><string>ko</string></array>
  <key>CFBundleAllowMixedLocalizations</key><true/>
  <key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
  <key>NSHumanReadableCopyright</key><string>© 2026 Khamis Abdulkarim Ali · MIT License</string>
  <key>CFBundleDocumentTypes</key>
  <array>
    <dict>
      <key>CFBundleTypeName</key><string>Convertible file</string>
      <key>CFBundleTypeRole</key><string>Viewer</string>
      <!-- Alternate: listed under Open With, never takes over as the default app. -->
      <key>LSHandlerRank</key><string>Alternate</string>
      <key>LSItemContentTypes</key>
      <array>
        <string>public.image</string>
        <string>com.adobe.pdf</string>
        <string>org.openxmlformats.wordprocessingml.document</string>
        <string>org.openxmlformats.wordprocessingml.document.macroenabled</string>
        <string>org.openxmlformats.wordprocessingml.template</string>
        <string>com.microsoft.word.doc</string>
        <string>com.microsoft.word.dot</string>
        <string>public.rtf</string>
        <string>org.oasis-open.opendocument.text</string>
        <string>public.movie</string>
        <string>public.audio</string>
        <string>public.json</string>
        <string>public.yaml</string>
        <string>public.xml</string>
        <string>public.comma-separated-values-text</string>
        <string>public.tab-separated-values-text</string>
        <string>net.daringfireball.markdown</string>
        <string>public.html</string>
        <string>public.plain-text</string>
        <string>public.zip-archive</string>
        <string>public.tar-archive</string>
        <string>org.gnu.gnu-zip-tar-archive</string>
        <string>public.folder</string>
      </array>
    </dict>
  </array>
</dict>
</plist>
EOF

chmod +x "$CONTENTS/MacOS/Bakkwo"

# Sign so Apple Silicon will run it: codesign on a Mac, or ldid when cross-building on Linux.
if command -v codesign >/dev/null; then
  codesign --force --deep --sign - "$APP"
elif command -v ldid >/dev/null; then
  ldid -S "$CONTENTS/MacOS/Bakkwo"
else
  echo "warning: no codesign/ldid found; sign before running on Apple Silicon" >&2
fi

rm -rf "$OUT/bin"
echo "Built $APP ($VERSION, $RID)"
