# Publish the three supported host binaries. Run this on any OS with the .NET 8+ SDK.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/NexusVM.UI/NexusVM.UI.csproj"

foreach ($rid in @("win-x64", "linux-x64", "osx-arm64")) {
    dotnet publish $project `
        --configuration Release `
        --runtime $rid `
        --self-contained true `
        --output (Join-Path $root "artifacts/$rid")
}
