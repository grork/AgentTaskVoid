<#
.SYNOPSIS
    Stages an isolated GitHub Copilot CLI host-event capture plugin.

.DESCRIPTION
    Builds HostEventRecorder, creates a local plugin inside a scratch git
    repository, substitutes the recorder's absolute path into the plugin's
    hooks declaration, and writes an activation script for the capture
    environment variables.

    The staged plugin is loaded only when Copilot CLI is launched with the
    returned --plugin-dir path. This script never reads or changes
    ~/.copilot/settings.json, ~/.copilot/hooks, or installed plugins.

.PARAMETER Configuration
    Build configuration for HostEventRecorder. Defaults to Debug.

.PARAMETER ScratchDir
    Scratch git repository to stage into. Defaults to a timestamped directory
    under $env:TEMP.

.PARAMETER SessionId
    Capture-file id. Defaults to copilot-<yyyyMMdd-HHmmss>.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Debug",
    [string]$ScratchDir,
    [string]$SessionId
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..\..")).Path
$recorderProject = Join-Path $repoRoot "tools\host-event-recorder\HostEventRecorder.csproj"
$pluginTemplate = Join-Path $PSScriptRoot "plugin.template.json"
$hooksTemplate = Join-Path $PSScriptRoot "hooks.template.json"

foreach ($requiredPath in @($recorderProject, $pluginTemplate, $hooksTemplate)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required capture artifact not found: '$requiredPath'."
    }
}

Write-Host "==> Building HostEventRecorder ($Configuration)..." -ForegroundColor Cyan
& dotnet build $recorderProject -c $Configuration --nologo | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed (exit $LASTEXITCODE)."
}

$recorderExe = Join-Path $repoRoot "tools\host-event-recorder\bin\$Configuration\net10.0\host-event-recorder.exe"
if (-not (Test-Path -LiteralPath $recorderExe)) {
    throw "Expected recorder executable not found at '$recorderExe'."
}
$recorderExe = (Resolve-Path -LiteralPath $recorderExe).Path

if (-not $ScratchDir) {
    $ScratchDir = Join-Path $env:TEMP ("atv-hostrec-copilot-cli-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
}
New-Item -ItemType Directory -Force -Path $ScratchDir | Out-Null
$ScratchDir = (Resolve-Path -LiteralPath $ScratchDir).Path

if (-not (Test-Path -LiteralPath (Join-Path $ScratchDir ".git"))) {
    Push-Location $ScratchDir
    try {
        & git init --initial-branch=main | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "git init failed (exit $LASTEXITCODE)."
        }
    }
    finally {
        Pop-Location
    }
}

$pluginDir = Join-Path $ScratchDir "copilot-hostrec-plugin"
$hooksDir = Join-Path $pluginDir "hooks"
New-Item -ItemType Directory -Force -Path $hooksDir | Out-Null

Copy-Item -LiteralPath $pluginTemplate -Destination (Join-Path $pluginDir "plugin.json") -Force

$powerShellEscapedExePath = $recorderExe.Replace("'", "''")
$jsonEscapedExePath = $powerShellEscapedExePath.Replace("\", "\\").Replace('"', '\"')
$hooksText = (Get-Content -Raw -LiteralPath $hooksTemplate).Replace("__RECORDER_EXE_PATH__", $jsonEscapedExePath)
$null = $hooksText | ConvertFrom-Json
Set-Content -LiteralPath (Join-Path $hooksDir "hooks.json") -Value $hooksText -NoNewline -Encoding utf8

$sandboxReadme = Join-Path $ScratchDir "README.md"
if (-not (Test-Path -LiteralPath $sandboxReadme)) {
    @"
# Agentaskvoid Copilot CLI capture sandbox

Disposable git repository used to exercise GitHub Copilot CLI hooks without
touching the AppTaskInfoCli working tree or personal Copilot configuration.
"@ | Set-Content -LiteralPath $sandboxReadme -Encoding utf8
}

$samplePath = Join-Path $ScratchDir "sample.txt"
if (-not (Test-Path -LiteralPath $samplePath)) {
    "alpha beta gamma" | Set-Content -LiteralPath $samplePath -Encoding utf8
}

$scriptsDir = Join-Path $ScratchDir "scripts"
New-Item -ItemType Directory -Force -Path $scriptsDir | Out-Null
$failureScript = Join-Path $scriptsDir "fail.ps1"
if (-not (Test-Path -LiteralPath $failureScript)) {
    "Write-Error 'Intentional host-event capture failure'; exit 7" |
        Set-Content -LiteralPath $failureScript -Encoding utf8
}
$slowScript = Join-Path $scriptsDir "slow.ps1"
if (-not (Test-Path -LiteralPath $slowScript)) {
    "Start-Sleep -Seconds 4; Write-Output 'background shell complete'" |
        Set-Content -LiteralPath $slowScript -Encoding utf8
}

if (-not $SessionId) {
    $SessionId = "copilot-" + (Get-Date -Format "yyyyMMdd-HHmmss")
}
$captureDir = Join-Path $repoRoot "tools\host-event-recorder\captures"
New-Item -ItemType Directory -Force -Path $captureDir | Out-Null
$captureDir = (Resolve-Path -LiteralPath $captureDir).Path

$env:HOSTREC_SESSION = $SessionId
$env:HOSTREC_CAPTURE_DIR = $captureDir

function ConvertTo-SingleQuotedPowerShellLiteral {
    param([string]$Value)
    return "'" + $Value.Replace("'", "''") + "'"
}

$activatePath = Join-Path $ScratchDir "activate.ps1"
$sessionLiteral = ConvertTo-SingleQuotedPowerShellLiteral $SessionId
$captureLiteral = ConvertTo-SingleQuotedPowerShellLiteral $captureDir
@"
`$env:HOSTREC_SESSION = $sessionLiteral
`$env:HOSTREC_CAPTURE_DIR = $captureLiteral
"@ | Set-Content -LiteralPath $activatePath -Encoding utf8

$captureFile = Join-Path $captureDir ("session-{0}.jsonl" -f $SessionId)

Write-Host "==> Copilot capture sandbox staged" -ForegroundColor Cyan
Write-Host "    Scratch repo:  $ScratchDir"
Write-Host "    Plugin dir:    $pluginDir"
Write-Host "    Recorder exe:  $recorderExe"
Write-Host "    Capture file:  $captureFile"
Write-Host "    Activate file: $activatePath"
Write-Host ""
Write-Host "Interactive launch (from a fresh PowerShell terminal):" -ForegroundColor Yellow
Write-Host "  . '$activatePath'"
Write-Host "  Set-Location '$ScratchDir'"
Write-Host "  copilot --plugin-dir '$pluginDir' --no-auto-update"

[pscustomobject]@{
    ScratchDir     = $ScratchDir
    PluginDir      = $pluginDir
    RecorderExe    = $recorderExe
    SessionId      = $SessionId
    CaptureDir     = $captureDir
    CaptureFile    = $captureFile
    ActivateScript = $activatePath
}
