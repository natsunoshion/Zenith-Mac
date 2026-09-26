#!/bin/zsh
set -eu
TASK_ROOT=${0:A:h:h}
cd "$TASK_ROOT"
TASK_ARCH=${1:-osx-arm64}
TASK_APP="$TASK_ROOT/dist/Zenith.app"
dotnet publish src/Zenith.Mac -c Release -r "$TASK_ARCH" --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o "$TASK_APP/Contents/MacOS"
mkdir -p "$TASK_APP/Contents/Resources/Zenith"
# Only explicitly installed macOS SDK modules may bring managed DLLs into the
# resource bundle. Keep Windows binaries excluded everywhere else, including
# executable helpers accidentally placed alongside a macOS module.
rsync -a --exclude='*.[eE][xX][eE]' --exclude='*.[bB][aA][tT]' --exclude='*.[cC][mM][dD]' \
  --exclude='*.dat' --exclude='lib/' --exclude='Updates/' --exclude='Settings/' \
  --include='/Plugins/Mac/***' --exclude='*.[dD][lL][lL]' \
  assets/windows/Zenith/ "$TASK_APP/Contents/Resources/Zenith/"
cp upstream/LICENSE "$TASK_APP/Contents/Resources/UPSTREAM_LICENSE"
cp THIRD_PARTY_NOTICES.md "$TASK_APP/Contents/Resources/"
mkdir -p "$TASK_APP/Contents/Resources/licenses"
cp licenses/* "$TASK_APP/Contents/Resources/licenses/"
xcrun actool --compile "$TASK_APP/Contents/Resources" --platform macosx --minimum-deployment-target 12.0 \
  --app-icon AppIcon --output-partial-info-plist /tmp/Zenith-Mac-AppIcon-Info.plist \
  src/Zenith.Mac/Assets/Assets.xcassets
cp src/Zenith.Mac/Assets/Zenith.icns "$TASK_APP/Contents/Resources/Zenith.icns"
cat > "$TASK_APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>Zenith</string>
<key>CFBundleDisplayName</key><string>Zenith</string>
<key>CFBundleIdentifier</key><string>com.github.natsunoshion.zenithmac</string>
<key>CFBundleVersion</key><string>3.0.0</string>
<key>CFBundleShortVersionString</key><string>3.0.0</string>
<key>CFBundleExecutable</key><string>Zenith.Mac</string>
<key>CFBundleIconFile</key><string>AppIcon</string>
<key>CFBundleIconName</key><string>AppIcon</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>LSMinimumSystemVersion</key><string>12.0</string>
<key>NSHighResolutionCapable</key><true/>
<key>NSPrincipalClass</key><string>NSApplication</string>
</dict></plist>
PLIST
codesign --force --deep --sign - "$TASK_APP"
# Notify ordinary bundle metadata consumers that this in-place build changed.
touch "$TASK_APP"
print "Built $TASK_APP ($TASK_ARCH)"
