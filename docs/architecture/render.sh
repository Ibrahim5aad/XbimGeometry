#!/usr/bin/env bash
# Renders all .d2 diagrams in this directory to SVG and PNG.
# Usage: ./render.sh

set -euo pipefail
cd "$(dirname "$0")"

for src in *.d2; do
  name="${src%.d2}"
  echo "Rendering $src ..."
  d2 --layout=dagre "$src" "${name}.svg"
  d2 --layout=dagre "$src" "${name}.png"
done

echo "Done."
