<#
Builds cvx-rando.exe (Biorand++) as one self-contained file (players install nothing) and copies it, with
the licence notices, next to recvx.exe in recvx-pc's build folder, where make_share.py picks it
up for the share zip.

    powershell -File tools/publish.ps1 [-Target <folder>] [-NoCopy]

-Target   where to copy cvx-rando.exe (default: ..\recvx-pc\build_pc)
-NoCopy   only build (the result is in artifacts\publish)
#>
param(
    [string]$Target = (Join-Path $PSScriptRoot "..\..\recvx-pc\build_pc"),
    [switch]$NoCopy
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$out = Join-Path $root "artifacts\publish"

# Not trimmed (WPF, and the data files are read with reflection-based JSON), so the runtime is
# included whole (compressed); WPF's native libraries are packed in and unpacked on first run.
dotnet publish (Join-Path $root "src\Biorand.App\Biorand.App.csproj") `
    -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# The notices travel with the exe (BioRand's and the libraries' licences ask for it).
$notices = Join-Path $out "cvx-rando-notices.txt"
(Get-Content (Join-Path $root "LICENSE") -Raw) + "`r`n`r`n" + (Get-Content (Join-Path $root "THIRD_PARTY_NOTICES.md") -Raw) +
    "`r`n`r`n" + (Get-Content (Join-Path $root "THIRD_PARTY_NOTICES_DGMLBUILDER.txt") -Raw) |
    Set-Content $notices -Encoding utf8

$exe = Join-Path $out "cvx-rando.exe"
"{0} ({1:N1} MB)" -f $exe, ((Get-Item $exe).Length / 1MB)

if (-not $NoCopy) {
    if (-not (Test-Path (Join-Path $Target "recvx.exe"))) {
        Write-Warning "No recvx.exe in $Target; copying anyway."
    }
    New-Item -ItemType Directory -Force $Target | Out-Null
    Copy-Item $exe, $notices $Target -Force
    "copied to $(Resolve-Path $Target)"
}
