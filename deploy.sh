#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
DOTNET=/usr/local/share/dotnet/dotnet
PROJECT_06="$SCRIPT_DIR/WirelessKitAddon-0.6.x/WirelessKitAddon.csproj"
PROJECT_UX="$SCRIPT_DIR/WirelessKitAddon.UX.Desktop/WirelessKitAddon.UX.Desktop.csproj"
PLUGIN_DIR="$HOME/Library/Application Support/OpenTabletDriver/Plugins/Wireless Kit Addon"

# Detect RID
ARCH=$(uname -m)
case "$ARCH" in
    arm64) RID="osx-arm64" ;;
    x86_64) RID="osx-x64" ;;
    *) echo "Unsupported architecture: $ARCH"; exit 1 ;;
esac

echo "Publishing 0.6.x plugin (Release)..."
$DOTNET publish "$PROJECT_06" -c Release -o /tmp/WirelessKitAddon-publish -v quiet

echo "Publishing tray app (Release, $RID)..."
$DOTNET publish "$PROJECT_UX" -c Release -r "$RID" -o /tmp/WirelessKitAddon-ux-publish -v quiet

echo "Deploying to OTD plugins..."
mkdir -p "$PLUGIN_DIR"

# Copy plugin DLLs (exclude OTD host assemblies that are already loaded)
OTD_DLLS="OpenTabletDriver.dll OpenTabletDriver.Plugin.dll OpenTabletDriver.Desktop.dll OpenTabletDriver.Native.dll OpenTabletDriver.Configurations.dll HidSharpCore.dll Newtonsoft.Json.dll Microsoft.Extensions.DependencyInjection.dll Microsoft.Extensions.DependencyInjection.Abstractions.dll"

for dll in /tmp/WirelessKitAddon-publish/*.dll; do
    name=$(basename "$dll")
    if ! echo "$OTD_DLLS" | grep -qw "$name"; then
        ditto "$dll" "$PLUGIN_DIR/$name"
    fi
done

# TrayManager expects the binary as "WirelessKitBatteryStatus.UX" (no extension on macOS)
ditto /tmp/WirelessKitAddon-ux-publish/WirelessKitBatteryStatus.UX "$PLUGIN_DIR/WirelessKitBatteryStatus.UX"
chmod +x "$PLUGIN_DIR/WirelessKitBatteryStatus.UX"
codesign --force --sign - "$PLUGIN_DIR/WirelessKitBatteryStatus.UX"

echo "Done. Restart OTD to load the new plugin."
