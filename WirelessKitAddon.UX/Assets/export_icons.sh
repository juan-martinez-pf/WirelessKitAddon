#!/bin/bash
# Exports all icon layers from Template.psd and converts them to multi-resolution .ico files.
# Layer names in the PSD are used as output filenames (e.g. "battery_100" → battery_100.ico).
# Layer 0 (the PSD composite) is skipped.

set -euo pipefail
cd "$(dirname "$0")"

PSD="Template.psd"
LAYER_COUNT=$(magick identify -format "%n\n" "$PSD" | head -1)

for (( i=1; i<LAYER_COUNT; i++ )); do
    name=$(magick identify -format "%[label]" "${PSD}[$i]")

    echo "Exporting layer $i → ${name}.ico"

    # Extract layer as PNG on a 256x256 canvas, preserving PSD layer offset
    magick "${PSD}[$i]" -repage 256x256 -background none -flatten "${name}.png"

    # Convert to multi-resolution ICO
    magick "${name}.png" -filter Lanczos -define icon:auto-resize=256,128,64,48,32,16 "${name}.ico"

    # Clean up intermediate PNG
    rm "${name}.png"
done

echo "Done. Exported $((LAYER_COUNT - 1)) icons."
