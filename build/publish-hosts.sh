#!/usr/bin/env bash
# Publish the three supported host binaries. Run this on any OS with the .NET 8+ SDK.
# Avalonia's native libraries stay next to the app, so the output is a directory per RID
# rather than a single file.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
project="$root/src/BlueStacks2.App/BlueStacks2.App.csproj"

for rid in win-x64 linux-x64 osx-arm64; do
  dotnet publish "$project" \
    --configuration Release \
    --runtime "$rid" \
    --self-contained true \
    --output "$root/artifacts/$rid"
done
