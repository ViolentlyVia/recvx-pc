<#
Builds recvx-pc and its randomizer in one go. It checks the tools the build needs (and offers to
install missing ones with winget), fetches the submodules, then builds recvx.exe and
recvx-config.exe (pc/tools/build.py) and cvx-rando.exe (rando/tools/publish.ps1). Everything
ends up in build_pc.

    build.bat                                  (double-click, or from a command prompt)
    powershell -ExecutionPolicy Bypass -File build.ps1 [-Version <tag>] [-Dir <folder>] [-NoRando] [-Yes]

Inside a recvx-pc checkout it builds that checkout. On its own (as downloaded from a release) it
first clones recvx-pc at -Version (default: the release it came with) into -Dir (default: a
recvx-pc folder next to the script).

-NoRando  only the game (no .NET SDK needed)
-Yes      install missing tools without asking
#>
param(
    [string]$Version = "v0.9.95",
    [string]$Dir = (Join-Path $PSScriptRoot "recvx-pc"),
    [switch]$NoRando,
    [switch]$Yes
)
$ErrorActionPreference = "Stop"
$Repo = "https://github.com/ViolentlyVia/recvx-pc.git"
$VsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"

function Step([string]$text) { Write-Host ""; Write-Host "== $text" -ForegroundColor Cyan }
function Fail([string]$text) { Write-Host ""; Write-Host $text -ForegroundColor Red; exit 1 }

function Ask([string]$question) {
    if ($Yes) { return $true }
    return (Read-Host "$question [y/N]") -match '^(y|yes)$'
}

function Refresh-Path {
    $env:Path = [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
                [Environment]::GetEnvironmentVariable("Path", "User")
}

# A tool: $Test returns true when it is there; else winget installs $Id (with $Extra), or the
# player is sent to $Url.
function Need([string]$Name, [scriptblock]$Test, [string]$Id, [string[]]$Extra, [string]$Url) {
    if (& $Test) { Write-Host "  $Name - found"; return }
    Write-Host "  $Name - missing" -ForegroundColor Yellow
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        Fail "$Name is needed. Install it from $Url, then run this again."
    }
    if (-not (Ask "  Install $Name with winget?")) {
        Fail "$Name is needed. Install it (winget install --id $Id, or $Url), then run this again."
    }
    & winget install --id $Id -e --accept-package-agreements --accept-source-agreements @Extra
    Refresh-Path
    if (-not (& $Test)) {
        Fail "$Name was installed but is not found yet. Open a new command prompt and run this again."
    }
}

function Find-Python {
    foreach ($cmd in @(@("py", "-3"), @("python"))) {
        if (-not (Get-Command $cmd[0] -ErrorAction SilentlyContinue)) { continue }
        $args_ = @($cmd | Select-Object -Skip 1)
        try { $v = & $cmd[0] @args_ -c "import sys; print(sys.version_info[0])" 2>$null } catch { continue }
        if ($v -eq "3") { return , $cmd }
    }
    return $null
}

Write-Host "recvx-pc build ($Version)" -ForegroundColor Green

Step "Checking the tools"
Need "Git" { [bool](Get-Command git -ErrorAction SilentlyContinue) } "Git.Git" @() "https://git-scm.com/download/win"
Need "Python 3" { $null -ne (Find-Python) } "Python.Python.3.12" @() "https://www.python.org/downloads/"
Need "Visual Studio 2022 C++ build tools (x86)" {
    (Test-Path $VsWhere) -and [bool](& $VsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)
} "Microsoft.VisualStudio.2022.BuildTools" @("--override", "--wait --passive --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended") "https://visualstudio.microsoft.com/visual-cpp-build-tools/"
if (-not $NoRando) {
    Need ".NET 10 SDK" {
        (Get-Command dotnet -ErrorAction SilentlyContinue) -and [bool](& dotnet --list-sdks 2>$null | Where-Object { $_ -match '^10\.' })
    } "Microsoft.DotNet.SDK.10" @() "https://dotnet.microsoft.com/download/dotnet/10.0"
}
$Py = Find-Python

Step "Getting the source"
if (Test-Path (Join-Path $PSScriptRoot "pc\tools\build.py")) {
    $Root = $PSScriptRoot
    Write-Host "  building this checkout: $Root"
} elseif (Test-Path (Join-Path $Dir "pc\tools\build.py")) {
    $Root = $Dir
    Write-Host "  using the existing checkout: $Root"
} else {
    # git keeps BioRand's own submodules about 60 characters deeper, and must stay under 260
    $full = [IO.Path]::GetFullPath($Dir)
    if ($full.Length -gt 120) {
        Fail "The folder path is too long for git ($($full.Length) characters): $full`nMove this script to a shorter folder (such as C:\recvx) or pass -Dir C:\recvx\recvx-pc."
    }
    Write-Host "  cloning $Repo ($Version) into $Dir"
    & git -c core.longpaths=true clone --recursive --branch $Version $Repo $Dir
    if ($LASTEXITCODE -ne 0) { Fail "git clone failed." }
    $Root = $Dir
    & git -C $Dir config core.longpaths true
}
$Build = Join-Path $Root "build_pc"
$running = Get-Process recvx, recvx-config, cvx-rando -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($Build, [StringComparison]::OrdinalIgnoreCase) }
if ($running) {
    Fail ("Close " + (($running | ForEach-Object { $_.ProcessName + ".exe" } | Sort-Object -Unique) -join ", ") +
          " first: the build replaces it in $Build.")
}

Push-Location $Root
try {
    & git -c core.longpaths=true submodule update --init --recursive
    if ($LASTEXITCODE -ne 0) { Fail "Fetching the submodules failed." }

    Step "Building the game (recvx.exe, recvx-config.exe)"
    $pyArgs = @($Py | Select-Object -Skip 1) + @("pc\tools\build.py")
    & $Py[0] @pyArgs
    if ($LASTEXITCODE -ne 0) { Fail "The game's build failed (see above)." }

    if (-not $NoRando) {
        Step "Building the randomizer (cvx-rando.exe)"
        & powershell -NoProfile -ExecutionPolicy Bypass -File "rando\tools\publish.ps1"
        if ($LASTEXITCODE -ne 0) { Fail "The randomizer's build failed (see above)." }
    }
} finally {
    Pop-Location
}

Step "Done"
Write-Host "  The programs are in $Build"
Write-Host "  Next: run recvx-config.exe there and choose your disc image (US PS2, SLUS-20184, .iso)."
if (-not $Yes -and (Read-Host "  Open recvx-config.exe now? [y/N]") -match '^(y|yes)$') {
    Start-Process (Join-Path $Build "recvx-config.exe")
}
