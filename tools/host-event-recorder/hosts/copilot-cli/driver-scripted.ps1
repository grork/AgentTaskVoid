<#
.SYNOPSIS
    Drives the non-interactive GitHub Copilot CLI capture beats.

.DESCRIPTION
    Runs a real `copilot -p` session against the staged local plugin. Prompt
    mode disables project extensions by default, so this driver temporarily
    enables prompt-mode extensions for the child process. It uses --allow-all
    only for this scripted pass; real permission and elicitation beats remain
    for cue-script.ps1.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ScratchDir,

    [string]$Prompt = @"
Work only inside this disposable repository. Use tools rather than merely
describing the work:
1. List the repository files and read README.md and sample.txt.
2. Create generated.txt containing a reversed copy of the words in sample.txt,
   then read it back.
3. Run scripts\fail.ps1 once so a real tool failure is observed; do not repair
   that intentionally failing script.
4. Launch two subagents in parallel, one using the explore agent and one using
   the task agent. Ask each to inspect a different existing sandbox file and
   report one fact. Wait for both.
5. Finish with a short summary of the observed files.
"@
)

$ErrorActionPreference = "Stop"

$ScratchDir = (Resolve-Path -LiteralPath $ScratchDir).Path
$pluginDir = Join-Path $ScratchDir "copilot-hostrec-plugin"
$activatePath = Join-Path $ScratchDir "activate.ps1"

foreach ($requiredPath in @($pluginDir, $activatePath)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required staged artifact not found: '$requiredPath'. Run stage.ps1 first."
    }
}

. $activatePath

$previousPromptModeExtensions = $env:GITHUB_COPILOT_PROMPT_MODE_EXTENSIONS
$env:GITHUB_COPILOT_PROMPT_MODE_EXTENSIONS = "true"

Write-Host "==> Driving scripted Copilot CLI beats (capture '$($env:HOSTREC_SESSION)')..." -ForegroundColor Cyan
Push-Location $ScratchDir
try {
    & copilot --plugin-dir $pluginDir --no-auto-update --no-remote --no-remote-export --no-color --allow-all --no-ask-user -p $Prompt | Out-Host
    $exitCode = $LASTEXITCODE
}
finally {
    Pop-Location
    $env:GITHUB_COPILOT_PROMPT_MODE_EXTENSIONS = $previousPromptModeExtensions
}

if ($exitCode -ne 0) {
    Write-Warning "copilot -p exited with code $exitCode; the partial capture may still be useful."
}

$captureFile = Join-Path $env:HOSTREC_CAPTURE_DIR ("session-{0}.jsonl" -f $env:HOSTREC_SESSION)
Start-Sleep -Seconds 2
if (Test-Path -LiteralPath $captureFile) {
    $recordCount = (Get-Content -LiteralPath $captureFile).Count
    Write-Host "==> Capture contains $recordCount record(s): $captureFile" -ForegroundColor Green
}
else {
    Write-Warning "No capture file was produced at '$captureFile'. Check plugin loading and hook diagnostics before continuing."
}
