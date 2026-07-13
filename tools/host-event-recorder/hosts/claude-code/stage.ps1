<#
.SYNOPSIS
    Stage step for the Claude Code host-event capture conduit (phase-14 Part B,
    INFRA-28). Builds the recorder, creates a throwaway scratch capture project,
    substitutes the real recorder exe path into a COPY of
    settings.hooks.template.json written into the scratch dir's project-scoped
    .claude/settings.json, and mints + exports the capture session id and
    capture directory.

.DESCRIPTION
    Never touches the operator's real, user-wide Claude Code settings
    (~/.claude/settings.json) -- the conduit this produces is project-scoped,
    live only inside the scratch directory this script creates. The
    substituted config is NOT written anywhere under the repo tree.

    Dot-source this script so the two exported env vars
    (HOSTREC_SESSION / HOSTREC_CAPTURE_DIR) land in YOUR shell and are
    inherited by whatever `claude` process you launch afterwards:

        . .\stage.ps1

    Running it un-dot-sourced (`.\stage.ps1` or `powershell -File stage.ps1`)
    still produces a fully staged scratch dir and an `activate.ps1` inside it
    that sets the same two env vars for a *different* process/session to
    dot-source later -- useful when staging and driving happen in separate
    shells (e.g. this script run once, the interactive cue-script session
    started later in a fresh terminal).

.PARAMETER Configuration
    Build configuration for the recorder (default: Debug -- this is a
    diagnostics tool, not the AOT release artifact Gate A already covers).

.PARAMETER ScratchDir
    Overrides the default throwaway scratch directory
    ($env:TEMP\atv-hostrec-claude-code-<timestamp>). Never a path under the
    repo tree.

.PARAMETER SessionId
    Overrides the minted session id (default: cc-<yyyyMMdd-HHmmss>).

.OUTPUTS
    A [pscustomobject] with ScratchDir, SettingsPath, RecorderExe, SessionId,
    CaptureDir, ActivateScript -- printed as a summary and returned as
    pipeline output so a caller can capture it: $staged = . .\stage.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Debug",
    [string]$ScratchDir,
    [string]$SessionId
)

$ErrorActionPreference = "Stop"

# --- Resolve repo root (this script lives at
#     tools/host-event-recorder/hosts/claude-code/stage.ps1) -----------------
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..\..")).Path
$recorderProject = Join-Path $repoRoot "tools\host-event-recorder\HostEventRecorder.csproj"
$templatePath = Join-Path $PSScriptRoot "settings.hooks.template.json"

if (-not (Test-Path $recorderProject)) {
    throw "Recorder project not found at '$recorderProject' -- is this script still under tools/host-event-recorder/hosts/claude-code/?"
}
if (-not (Test-Path $templatePath)) {
    throw "Conduit template not found at '$templatePath'."
}

# --- 1. Build the recorder --------------------------------------------------
Write-Host "==> Building HostEventRecorder ($Configuration)..." -ForegroundColor Cyan
& dotnet build $recorderProject -c $Configuration | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed (exit $LASTEXITCODE) -- see output above."
}

$recorderExe = Join-Path $repoRoot "tools\host-event-recorder\bin\$Configuration\net10.0\host-event-recorder.exe"
if (-not (Test-Path $recorderExe)) {
    throw "Expected recorder exe not found at '$recorderExe' after build."
}
$recorderExe = (Resolve-Path $recorderExe).Path
Write-Host "    Recorder exe: $recorderExe"

# --- 2. Create the throwaway scratch capture project dir -------------------
if (-not $ScratchDir) {
    $ScratchDir = Join-Path $env:TEMP ("atv-hostrec-claude-code-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
}
New-Item -ItemType Directory -Force -Path $ScratchDir | Out-Null
$claudeDir = Join-Path $ScratchDir ".claude"
New-Item -ItemType Directory -Force -Path $claudeDir | Out-Null
Write-Host "==> Scratch project dir: $ScratchDir" -ForegroundColor Cyan

# --- 3. Substitute the real exe path into a COPY of the template -----------
# JSON-escape the path (double backslashes) before substitution -- this is a
# plain token replacement, not a JSON round-trip, so the rest of the template
# bytes are untouched.
$templateText = Get-Content -Raw -Path $templatePath
# NOTE: the replacement-string argument to -replace is NOT regex-escaped by
# .NET (only $-group references are special there), so '\\' as a literal
# 2-backslash PowerShell string is exactly right: each matched single
# backslash becomes two literal backslashes -- one level of JSON-string
# escaping. Writing '\\\\' here (4 chars) would double-escape and leave
# each backslash JSON-escaped TWICE, which still parses as valid JSON but
# resolves to a path containing literal doubled backslashes -- verified by
# hand during this phase's mechanical validation.
$escapedExePath = $recorderExe -replace '\\', '\\'
$settingsText = $templateText -replace [regex]::Escape("__RECORDER_EXE_PATH__"), $escapedExePath

# Sanity: the substituted text must still be valid JSON before we write it.
$null = $settingsText | ConvertFrom-Json

$settingsPath = Join-Path $claudeDir "settings.json"
Set-Content -Path $settingsPath -Value $settingsText -NoNewline -Encoding utf8
Write-Host "    Substituted conduit config: $settingsPath"

# --- 4. Mint + export the session id and capture dir ------------------------
if (-not $SessionId) {
    $SessionId = "cc-" + (Get-Date -Format "yyyyMMdd-HHmmss")
}
$captureDir = Join-Path $repoRoot "tools\host-event-recorder\captures"
New-Item -ItemType Directory -Force -Path $captureDir | Out-Null
$captureDir = (Resolve-Path $captureDir).Path

# Exported into THIS process's environment -- inherited by any child process
# (e.g. `claude`) launched afterwards in the same shell. If this script was
# NOT dot-sourced, these only affect this script's own (now-exiting) process;
# activate.ps1 below is the fallback for that case.
$env:HOSTREC_SESSION = $SessionId
$env:HOSTREC_CAPTURE_DIR = $captureDir

$activatePath = Join-Path $ScratchDir "activate.ps1"
@"
# Generated by stage.ps1 -- dot-source this in any shell that needs the same
# capture session/dir without re-running the stage step, e.g.:
#   . '$activatePath'
`$env:HOSTREC_SESSION = '$SessionId'
`$env:HOSTREC_CAPTURE_DIR = '$captureDir'
"@ | Set-Content -Path $activatePath -Encoding utf8

Write-Host "==> Session id:   $SessionId"
Write-Host "    Capture dir:  $captureDir"
Write-Host "    Activate script (for a different shell): $activatePath"
Write-Host ""
Write-Host "Next: cd into the scratch dir and drive Claude Code from there so it" -ForegroundColor Yellow
Write-Host "picks up the project-scoped .claude\settings.json staged above, e.g.:" -ForegroundColor Yellow
Write-Host "  Set-Location '$ScratchDir'; claude -p `"...`"" -ForegroundColor Yellow

[pscustomobject]@{
    ScratchDir     = $ScratchDir
    SettingsPath   = $settingsPath
    RecorderExe    = $recorderExe
    SessionId      = $SessionId
    CaptureDir     = $captureDir
    ActivateScript = $activatePath
}
