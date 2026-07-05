<#
.SYNOPSIS
  One-shot dev environment bootstrap for the OTT Platform (Windows).

.DESCRIPTION
  Installs core tools (Git, .NET 8 SDK, Docker Desktop, Flutter), sets up PATH/env, starts the
  SQL Server + Redis containers, applies EF migrations, and runs `flutter pub get`. Idempotent —
  re-running skips anything already present. Optional heavy toolchains (Android Studio, VS 2022 C++)
  are opt-in via switches.

.PARAMETER FlutterPath
  Where to install Flutter if it isn't already on PATH. Default C:\src\flutter.

.PARAMETER FlutterVersion
  Flutter stable tag to check out. Default 3.41.9 (the version this project builds with).

.PARAMETER IncludeAndroid
  Also install Android Studio (you still finish SDK setup in its SDK Manager).

.PARAMETER IncludeWindowsDesktop
  Also install Visual Studio 2022 Community with the Desktop C++ workload (Windows desktop target).

.PARAMETER SkipServices
  Do not run `docker compose up` for SQL Server + Redis.

.PARAMETER SkipDatabase
  Do not run the EF Core migrations.

.EXAMPLE
  # From an elevated PowerShell, in the ott-platform folder:
  .\bootstrap.ps1
  .\bootstrap.ps1 -IncludeAndroid
  .\bootstrap.ps1 -SkipServices -SkipDatabase        # tools only
#>
[CmdletBinding()]
param(
    [string]$FlutterPath = 'C:\src\flutter',
    [string]$FlutterVersion = '3.41.9',
    [switch]$IncludeAndroid,
    [switch]$IncludeWindowsDesktop,
    [switch]$SkipServices,
    [switch]$SkipDatabase
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$apiDir = Join-Path $repoRoot 'dotnet_api'
$appDir = Join-Path $repoRoot 'flutter_app'

function Write-Step($msg)  { Write-Host "`n=== $msg ===" -ForegroundColor Green }
function Write-Info($msg)  { Write-Host "  $msg" -ForegroundColor Gray }
function Write-Skip($msg)  { Write-Host "  [skip] $msg" -ForegroundColor DarkGray }
function Write-Warn2($msg) { Write-Host "  [warn] $msg" -ForegroundColor Yellow }

function Update-SessionPath {
    $m = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $u = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = "$m;$u"
}

function Add-ToUserPath($dir) {
    $u = [Environment]::GetEnvironmentVariable('Path', 'User')
    if ($u -notlike "*$dir*") {
        [Environment]::SetEnvironmentVariable('Path', "$u;$dir", 'User')
        Write-Info "Added to user PATH: $dir"
    }
    if ($env:Path -notlike "*$dir*") { $env:Path = "$env:Path;$dir" }
}

function Install-IfMissing($WingetId, $ProbeCmd, $Name, $ExtraArgs) {
    if (Get-Command $ProbeCmd -ErrorAction SilentlyContinue) { Write-Skip "$Name already installed"; return }
    Write-Info "Installing $Name ($WingetId) ..."
    $args = @('install', '--id', $WingetId, '-e', '--accept-source-agreements', '--accept-package-agreements')
    if ($ExtraArgs) { $args += $ExtraArgs }
    try { winget @args } catch { Write-Warn2 "winget for $Name returned an error (may already be installed): $($_.Exception.Message)" }
    Update-SessionPath
}

# ── Preflight ────────────────────────────────────────────────────────────────
Write-Step 'Preflight'
if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
    throw 'winget (App Installer) not found. Install "App Installer" from the Microsoft Store, then re-run.'
}
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
if (-not $isAdmin) { Write-Warn2 'Not running as Administrator — Docker Desktop / Visual Studio installs may prompt or fail. Consider re-running elevated.' }
Write-Info "Repo root: $repoRoot"

# ── Phase 1: core tools ──────────────────────────────────────────────────────
Write-Step 'Phase 1 — Core tools'
Install-IfMissing 'Git.Git'                'git'    'Git'
Install-IfMissing 'Microsoft.DotNet.SDK.8' 'dotnet' '.NET 8 SDK'
Install-IfMissing 'Docker.DockerDesktop'   'docker' 'Docker Desktop'

# ── Phase 2: Flutter ─────────────────────────────────────────────────────────
Write-Step 'Phase 2 — Flutter SDK'
if (Get-Command flutter -ErrorAction SilentlyContinue) {
    Write-Skip "Flutter already on PATH ($(flutter --version 2>$null | Select-Object -First 1))"
} else {
    if (-not (Test-Path (Join-Path $FlutterPath 'bin\flutter.bat'))) {
        Write-Info "Cloning Flutter into $FlutterPath ..."
        New-Item -ItemType Directory -Force -Path (Split-Path $FlutterPath) | Out-Null
        git clone https://github.com/flutter/flutter.git -b stable $FlutterPath
        try { git -C $FlutterPath checkout $FlutterVersion } catch { Write-Warn2 "Tag $FlutterVersion not found; staying on stable channel." }
    }
    Add-ToUserPath (Join-Path $FlutterPath 'bin')
}
try { flutter --version | Select-Object -First 1 } catch { Write-Warn2 'Flutter not resolvable in this session yet — open a new terminal after this script.' }

# ── Phase 3: optional platform toolchains ────────────────────────────────────
Write-Step 'Phase 3 — Platform toolchains (optional)'
if ($IncludeAndroid) {
    Install-IfMissing 'Google.AndroidStudio' 'studio' 'Android Studio'
    Write-Warn2 'Finish in Android Studio > SDK Manager: install SDK Platform 34, Platform-Tools, Build-Tools 34, Emulator + a system image. Then set ANDROID_HOME / JAVA_HOME and run: flutter doctor --android-licenses'
} else { Write-Skip 'Android Studio (pass -IncludeAndroid to install)' }

if ($IncludeWindowsDesktop) {
    Install-IfMissing 'Microsoft.VisualStudio.2022.Community' 'devenv' 'Visual Studio 2022' @('--override', '--quiet --wait --add Microsoft.VisualStudio.Workload.NativeDesktop --includeRecommended')
} else { Write-Skip 'Visual Studio C++ (pass -IncludeWindowsDesktop to install)' }

# ── Phase 4: EF Core tools ───────────────────────────────────────────────────
Write-Step 'Phase 4 — EF Core CLI'
Add-ToUserPath (Join-Path $env:USERPROFILE '.dotnet\tools')
if (Get-Command dotnet-ef -ErrorAction SilentlyContinue) {
    Write-Skip 'dotnet-ef already installed'
} else {
    try { dotnet tool install --global dotnet-ef } catch { Write-Warn2 "dotnet-ef install failed: $($_.Exception.Message)" }
    Update-SessionPath
}

# ── Phase 5: backend services ────────────────────────────────────────────────
Write-Step 'Phase 5 — SQL Server + Redis containers'
if ($SkipServices) {
    Write-Skip 'Services skipped (-SkipServices)'
} else {
    $dockerUp = $false
    try { docker info *> $null; $dockerUp = $? } catch { $dockerUp = $false }
    if (-not $dockerUp) {
        Write-Warn2 'Docker daemon not reachable. Start Docker Desktop (first launch finishes WSL2 setup), then re-run with: .\bootstrap.ps1 -SkipServices:$false'
    } else {
        Push-Location $repoRoot
        try { docker compose up -d sqlserver redis; Write-Info 'Waiting ~20s for SQL Server to become healthy...'; Start-Sleep -Seconds 20 }
        finally { Pop-Location }
    }
}

# ── Phase 6: database schema ─────────────────────────────────────────────────
Write-Step 'Phase 6 — Database migrations'
if ($SkipDatabase) {
    Write-Skip 'Migrations skipped (-SkipDatabase)'
} else {
    Push-Location $apiDir
    try {
        dotnet restore
        dotnet ef database update -p OTT.Infrastructure -s OTT.API
        Write-Info 'Schema applied.'
    } catch {
        Write-Warn2 "Migration failed (is SQL Server reachable? check the connection string): $($_.Exception.Message)"
    } finally { Pop-Location }
}

# ── Phase 7: Flutter packages ────────────────────────────────────────────────
Write-Step 'Phase 7 — Flutter packages'
if (Get-Command flutter -ErrorAction SilentlyContinue) {
    Push-Location $appDir
    try { flutter pub get } catch { Write-Warn2 "flutter pub get failed: $($_.Exception.Message)" } finally { Pop-Location }
} else {
    Write-Warn2 'Skipping pub get — open a NEW terminal (so PATH refreshes), then run: cd flutter_app; flutter pub get'
}

# ── Done ─────────────────────────────────────────────────────────────────────
Write-Step 'Done — next steps'
Write-Host @"
  1. Open a NEW terminal so PATH changes take effect, then: flutter doctor
  2. Copy .env.example to .env and adjust secrets (for the Docker full stack).
  3. Run the backend:   cd dotnet_api;  dotnet run --project OTT.API      # http://localhost:50664
  4. Run the app:       cd flutter_app; flutter run -d chrome --dart-define=API_BASE_URL=http://localhost:50664
  See SETUP.md for the full reference.
"@ -ForegroundColor Cyan
