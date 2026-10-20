#!/usr/bin/env bash
# Builds the Android app as an installable APK: publish/Bakkwo-<version>-android.apk
#
# Needs: the .NET 8 SDK with the Android workload (dotnet workload install android),
#        JDK 17, and the Android SDK (platform 34). Android Studio installs the last two.
#   ANDROID_HOME=~/Library/Android/sdk JAVA_HOME=<jdk17> ./build/package-android.sh
#
# Signing: with BAKKWO_KEYSTORE, BAKKWO_KEY_ALIAS and BAKKWO_KEY_PASS set, the APK is signed with
# your own key (needed for Google Play, and so updates install over each other). Otherwise it's
# signed with this computer's debug key, which is fine for installing on your own phone.
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)
ARGS=(-c Release -f net8.0-android)
[ -n "${ANDROID_HOME:-}" ] && ARGS+=("-p:AndroidSdkDirectory=$ANDROID_HOME")
[ -n "${JAVA_HOME:-}" ] && ARGS+=("-p:JavaSdkDirectory=$JAVA_HOME")
if [ -n "${BAKKWO_KEYSTORE:-}" ]; then
  ARGS+=(-p:AndroidKeyStore=true "-p:AndroidSigningKeyStore=$BAKKWO_KEYSTORE"
         "-p:AndroidSigningKeyAlias=${BAKKWO_KEY_ALIAS:?set BAKKWO_KEY_ALIAS}"
         "-p:AndroidSigningKeyPass=env:BAKKWO_KEY_PASS" "-p:AndroidSigningStorePass=env:BAKKWO_KEY_PASS")
fi

rm -rf publish/android
dotnet publish src/Bakkwo.Android "${ARGS[@]}" -o publish/android
mkdir -p publish
cp publish/android/*-Signed.apk "publish/Bakkwo-$VERSION-android.apk"
echo "Built publish/Bakkwo-$VERSION-android.apk"
