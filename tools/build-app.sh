#!/bin/zsh
set -eu
TASK_ROOT=${0:A:h:h}
cd "$TASK_ROOT"
TASK_ARCH=${1:-osx-arm64}
TASK_APP="$TASK_ROOT/dist/Zenith.app"
TASK_ICON_HASH=$(shasum -a 256 src/Zenith.Mac/Assets/Zenith.icns | awk '{print substr($1,1,12)}')
TASK_ICON_FILE="Zenith-$TASK_ICON_HASH.icns"
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
cp src/Zenith.Mac/Assets/Zenith.icns "$TASK_APP/Contents/Resources/$TASK_ICON_FILE"
cat > "$TASK_APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>Zenith</string>
<key>CFBundleDisplayName</key><string>Zenith</string>
<key>CFBundleIdentifier</key><string>org.zenithmidi.macos</string>
<key>CFBundleVersion</key><string>0.1.2</string>
<key>CFBundleShortVersionString</key><string>0.1.2</string>
<key>CFBundleExecutable</key><string>Zenith.Mac</string>
<key>CFBundleIconFile</key><string>Zenith.icns</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>LSMinimumSystemVersion</key><string>12.0</string>
<key>NSHighResolutionCapable</key><true/>
<key>NSPrincipalClass</key><string>NSApplication</string>
</dict></plist>
PLIST
/usr/libexec/PlistBuddy -c "Set :CFBundleIconFile $TASK_ICON_FILE" "$TASK_APP/Contents/Info.plist"
codesign --force --deep --sign - "$TASK_APP"
# Notify ordinary bundle metadata consumers that this in-place build changed.
touch "$TASK_APP"
print "Built $TASK_APP ($TASK_ARCH)"
